using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.ToolCollections.Context;
using static PinkRooster.ToolCollections.Tests.TestCollections;

namespace PinkRooster.ToolCollections.Tests.Context;

public sealed class ToolCollectionContextProviderTests
{
    [Fact]
    public async Task APlainAgentGetsTheToolsStandingTextAndContext_AndTheToolsRun()
    {
        Counter counter = new();
        ScriptedChatClient client = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Increment", new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = "Be brief." },
            AIContextProviders = [new ToolCollectionContextProvider(new Silent(), counter)]
        });

        AgentResponse response = await agent.RunAsync("go");

        Assert.Equal("done", response.Text);
        Assert.Equal(["Nothing", "Increment"], client.Options[0]!.Tools!.Select(tool => tool.Name));
        string instructions = client.Options[0]!.Instructions!.ReplaceLineEndings("\n");
        Assert.StartsWith("Be brief.", instructions);
        Assert.Contains("# Instructions\n- Nothing is ever done.\n\n# Constraints\n- Never do anything.\n\n", instructions);
        Assert.EndsWith($"{ToolCollectionChatClient.FramingLine}\n\n## Counter\n0", instructions);
        // The tool ran; context is read once per run, so the loop's second call still shows the count from the start of the run.
        Assert.Contains(client.Requests[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>(), result => result.Result?.ToString() == "1");
        Assert.Equal(client.Options[0]!.Instructions, client.Options[1]!.Instructions);
    }

    [Fact]
    public async Task ASessionStoresNoContext()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "one"), new ChatMessage(ChatRole.Assistant, "two"));
        AIAgent agent = new ChatClientAgent(client, new ChatClientAgentOptions { AIContextProviders = [new ToolCollectionContextProvider(new Counter())] });
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("a", session);
        await agent.RunAsync("b", session);

        Assert.True(session.TryGetInMemoryChatHistory(out List<ChatMessage>? stored));
        Assert.Equal(["a", "one", "b", "two"], stored!.Select(message => message.Text));
    }

    [Fact]
    public async Task AFailingCollectionIsLoggedAndSkipped()
    {
        RecordingLogger logger = new();
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            AIContextProviders = [new ToolCollectionContextProvider([new Failing(), new Counter()], logger)]
        });

        await agent.RunAsync("go");

        Assert.Single(logger.Errors);
        Assert.EndsWith("## Counter\n0", client.Options[0]!.Instructions!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task ContextOnlyModeSendsNoToolsOrStandingText()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            AIContextProviders = [ToolCollectionContextProvider.ForContextOnly([new Silent(), new Counter()], logger: null)]
        });

        await agent.RunAsync("go");

        Assert.Null(client.Options[0]!.Tools);
        Assert.Equal($"{ToolCollectionChatClient.FramingLine}\n\n## Counter\n0", client.Options[0]!.Instructions);
    }

    [Fact]
    public async Task AToolThatRequiresApprovalRunsOnlyAfterTheHostApproves()
    {
        Guarded guarded = new();
        ScriptedChatClient client = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Delete", new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = new ChatClientAgent(client, new ChatClientAgentOptions { AIContextProviders = [new ToolCollectionContextProvider(guarded)] });
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse asked = await agent.RunAsync("delete it", session);
        ToolApprovalRequestContent request = Assert.Single(asked.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Equal(0, guarded.Deletes);

        AgentResponse approved = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);

        Assert.Equal(1, guarded.Deletes);
        Assert.Equal("done", approved.Text);
    }

    [Fact]
    public void RejectsTheSameToolNameInTwoCollections_NamingBoth()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new ToolCollectionContextProvider(new Counter(), new Counter2()));

        Assert.Contains("'increment'", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(Counter), error.Message);
        Assert.Contains(nameof(Counter2), error.Message);
        Assert.Contains("rename one", error.Message);
    }

    private sealed class Counter2 : ToolCollection
    {
        [Tool("increment", "Adds one.")]
        public string Increment() => "1";
    }
}
