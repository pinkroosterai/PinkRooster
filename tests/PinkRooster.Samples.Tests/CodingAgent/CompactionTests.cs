using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using static PinkRooster.Samples.Tests.CodingAgent.Showcase;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>A long conversation is shortened on its way to the model, and saved whole.</summary>
public sealed class CompactionTests
{
    // About 360 tokens by MAF's estimate.
    private static string Long(string word) => string.Join(" ", Enumerable.Repeat(word, 240));

    private static Showcase WithContextSize(int? tokens) => Showcase.WithSettings(
        $"{{ \"models\": [ {{ \"model\": \"test-model\", \"endpoint\": \"http://localhost:1/v1\"{(tokens is null ? "" : $", \"contextSize\": {tokens}")} }} ] }}");

    private static int Size(List<ChatMessage> request) => request.Sum(message => message.Text.Length);

    private static string SavedSession(Showcase showcase) =>
        File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(showcase.Workspace, ".pinkrooster", "sessions"), "*.json")));

    [Fact]
    public async Task WithAContextSizeOf1000_ARequestPast750Tokens_IsCompactedOnce_AndTheSavedSessionStaysWhole()
    {
        using Showcase showcase = WithContextSize(1000);
        // Two long answers put the third request past the trigger; the model is then asked for the summary before it answers.
        ScriptedChatClient model = new(
            Text(Long("alpha")), Text(Long("bravo")),
            Text("THE SUMMARY OF THE EARLIER TALK"), Text("third answer"),
            Text("fourth answer"), Text("fifth answer"));

        await showcase.RunAsync(model, "one", "two", "three", "four", "five", "/status");

        // Five prompts and one summary: the summary request is the one that carries no tools.
        Assert.Equal(6, model.Requests.Count);
        Assert.Equal([2], Enumerable.Range(0, 6).Where(index => model.Options[index]?.Tools is not { Count: > 0 }));
        List<ChatMessage> third = model.Requests[3];
        Assert.Contains(third, message => message.Text.Contains("THE SUMMARY OF THE EARLIER TALK"));
        Assert.DoesNotContain(third, message => message.Text.Contains("alpha alpha"));
        Assert.Contains(third, message => message.Text == "three");
        // Uncompacted it would hold both long answers.
        Assert.True(Size(third) < Long("alpha").Length + Long("bravo").Length, "the compacted request is shorter than the conversation it stands for");
        // The summary is kept: the requests after it hold it again and no second summary was asked for.
        foreach (List<ChatMessage> later in model.Requests[4..])
        {
            Assert.Contains(later, message => message.Text.Contains("THE SUMMARY OF THE EARLIER TALK"));
            Assert.DoesNotContain(later, message => message.Text.Contains("alpha alpha"));
        }
        Assert.Contains(model.Requests[5], message => message.Text == "fourth answer");
        // What is saved is the whole conversation, without the summary.
        string saved = SavedSession(showcase);
        Assert.Contains("alpha alpha", saved);
        Assert.Contains("bravo bravo", saved);
        Assert.DoesNotContain("THE SUMMARY OF THE EARLIER TALK", saved);
        Assert.True(Size(third) < saved.Length);
        Assert.Contains("compacted past 750 tokens", showcase.Console.Output);
    }

    [Fact]
    public async Task WithoutAContextSize_NothingIsCompactedByItself_AndCompactDoesItForTheNextRequestAndAfter()
    {
        using Showcase showcase = WithContextSize(null);
        ScriptedChatClient model = new(
            Text(Long("alpha")), Text(Long("bravo")), Text(Long("charlie")), Text("fourth answer"),
            Text("THE SUMMARY ON REQUEST"), Text("fifth answer"),
            Text("sixth answer"));

        await showcase.RunAsync(model, "one", "two", "three", "four", "/compact", "five", "six");

        // Four requests went out whole, however long the conversation got.
        Assert.Contains(model.Requests[3], message => message.Text.Contains("alpha alpha"));
        Assert.Equal(7, model.Requests.Count);
        Assert.Contains("The next request is shortened", showcase.Console.Output);
        List<ChatMessage> fifth = model.Requests[5];
        Assert.Contains(fifth, message => message.Text.Contains("THE SUMMARY ON REQUEST"));
        Assert.DoesNotContain(fifth, message => message.Text.Contains("alpha alpha"));
        // It holds for the run after it too, without another summary.
        Assert.Contains(model.Requests[6], message => message.Text.Contains("THE SUMMARY ON REQUEST"));
        Assert.DoesNotContain(model.Requests[6], message => message.Text.Contains("alpha alpha"));
        Assert.Contains("alpha alpha", SavedSession(showcase));
    }

    [Fact]
    public async Task WhenTheSummaryCallFails_TheRequestGoesOutWhole_WithAWarning()
    {
        using Showcase showcase = WithContextSize(1000);
        FailsOnSummary model = new(Text(Long("alpha")), Text(Long("bravo")), Text("third answer"));

        int exit = await showcase.RunAsync(model, showcase.Console, "one", "two", "three");

        Assert.Equal(0, exit);
        Assert.Contains("could not be summarised (the summary service is down), so this request goes out whole.", showcase.Console.Output);
        Assert.Contains(model.Answered[^1], message => message.Text.Contains("alpha alpha"));
        Assert.Equal(3, model.Answered.Count);
    }

    // Answers the conversation from a script and fails every request that asks for a summary.
    private sealed class FailsOnSummary(params ChatMessage[] answers) : IChatClient
    {
        private readonly Queue<ChatMessage> script = new(answers);

        public List<List<ChatMessage>> Answered { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            List<ChatMessage> request = [.. messages];
            // The summarizer is called without the assistant's tools.
            if (options?.Tools is not { Count: > 0 })
            {
                throw new InvalidOperationException("the summary service is down");
            }
            Answered.Add(request);
            return Task.FromResult(new ChatResponse(script.Dequeue()));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ChatResponseUpdate update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
