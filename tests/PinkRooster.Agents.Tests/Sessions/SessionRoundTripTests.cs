using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Steps;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.Sessions;

/// <summary>
/// What a session store rests on: a session of an agent with a step loop, a background tool, an inbox and per-session collection
/// state can be saved, read back by another agent instance, and gone on with.
/// </summary>
public sealed class SessionRoundTripTests
{
    // A model that first notes something and starts a background task in one reply, and answers in text from then on.
    private sealed class NotingModel : IChatClient
    {
        public List<List<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            List<ChatMessage> request = [.. messages];
            lock (Requests)
            {
                Requests.Add(request);
            }
            bool noted = request.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Any();
            return Task.FromResult(new ChatResponse(noted
                ? new ChatMessage(ChatRole.Assistant, $"answer {Requests.Count}")
                : new ChatMessage(ChatRole.Assistant,
                [
                    new FunctionCallContent("call-note", "AddNote", new Dictionary<string, object?> { ["text"] = "the build is red" }),
                    new FunctionCallContent("call-quick", "Quick", new Dictionary<string, object?> { ["text"] = "it", ["runInBackground"] = true })
                ])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ChatResponseUpdate update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class NoteList
    {
        public List<string> Items { get; set; } = [];
    }

    // State per session, the way the per-session task list keeps its list.
    private sealed class Notes : ToolCollection
    {
        [Tool("AddNote", "Keeps a note.", Kind = ToolKind.State)]
        public string AddNote(string text)
        {
            NoteList list = SessionState(() => new NoteList());
            list.Items.Add(text);
            SetSessionState(list);
            return "noted";
        }

        public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>($"## Notes\n{string.Join("\n", SessionState(() => new NoteList()).Items)}");
    }

    [AgentRole("You work in steps.")]
    private sealed class Worker(IChatClient chatClient) : SteppedAgent(chatClient)
    {
        protected override int MaxTurns => 2;

        protected override void Configure(AgentBuilder agent) => agent
            .WithTools(new Notes())
            .WithTool(AIFunctionFactory.Create((string text) => $"did {text}", "Quick"))
            .AllowBackground("Quick")
            .WithInbox();

        protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NextStep.Send("second", "Go on."));
    }

    private static string[] Shape(List<ChatMessage> request) =>
        [.. request.Select(message => $"{message.Role}: {string.Join(" | ", message.Contents.Select(content => content switch
        {
            TextContent text => text.Text,
            FunctionCallContent call => $"call {call.Name}",
            FunctionResultContent result => $"result {result.CallId}",
            _ => content.GetType().Name
        }))}")];

    [Fact]
    public async Task ASessionOfASteppedAgentWithABackgroundToolAnInboxAndPerSessionState_GoesOnAfterARoundTrip()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        NotingModel firstModel = new();
        Worker first = new(firstModel);
        AgentSession session = await first.CreateSessionAsync(none);
        AgentResponse answered = await first.RunAsync("first prompt", session, cancellationToken: none);
        Assert.StartsWith("answer", answered.Text);

        JsonElement saved = await first.SerializeSessionAsync(session, cancellationToken: none);
        // Through text, as a file would hold it, and into another agent instance, as another process would have.
        JsonElement fromDisk = JsonDocument.Parse(saved.GetRawText()).RootElement.Clone();
        NotingModel secondModel = new();
        Worker second = new(secondModel);
        AgentSession restored = await second.DeserializeSessionAsync(fromDisk, cancellationToken: none);
        int before = firstModel.Requests.Count;

        await second.RunAsync("second prompt", restored, cancellationToken: none);
        await first.RunAsync("second prompt", session, cancellationToken: none);

        // The restored session's next request is the one the saved session's would have been.
        string[] afterRestore = Shape(secondModel.Requests[0]);
        Assert.Equal(Shape(firstModel.Requests[before]), afterRestore);
        Assert.Equal(1, afterRestore.Count(line => line == "user: first prompt"));
        Assert.Contains("user: second prompt", afterRestore);
        Assert.Contains(afterRestore, line => line.Contains("call AddNote") && line.Contains("call Quick"));
        // The per-session state came back: the collection's context still holds the note.
        Assert.Contains(afterRestore, line => line.Contains("the build is red"));
        // The step loop starts the new message at its first step: two turns again.
        Assert.Equal(firstModel.Requests.Count - before, secondModel.Requests.Count);
    }
}
