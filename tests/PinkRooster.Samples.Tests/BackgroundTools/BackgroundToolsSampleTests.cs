using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Samples.Tests.TestSupport;
using Sample = PinkRooster.Samples.BackgroundTools.Program;

namespace PinkRooster.Samples.Tests.BackgroundTools;

public sealed class BackgroundToolsSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string[] Products = ["Atlas", "Borealis", "Cirrus"];

    /// <summary>
    /// A model that answers from what the request holds, because how many model calls the run makes depends on when the tasks end:
    /// it starts the three counts, ends its turn until it was told that all three ended, reads the three results, and answers.
    /// </summary>
    private sealed class CountingModel : IChatClient
    {
        public List<List<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            List<ChatMessage> request = [.. messages];
            lock (Requests)
            {
                Requests.Add(request);
            }
            return Task.FromResult(new ChatResponse(Next(request)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ChatResponseUpdate update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }

        private static ChatMessage Next(List<ChatMessage> request)
        {
            FunctionCallContent[] calls = [.. request.SelectMany(message => message.Contents).OfType<FunctionCallContent>()];
            if (calls.Length == 0)
            {
                return new ChatMessage(ChatRole.Assistant,
                [
                    .. Products.Select((product, index) => new FunctionCallContent($"start{index}", "CountTickets",
                        new Dictionary<string, object?> { ["product"] = product, ["runInBackground"] = true }))
                ]);
            }

            int ended = request.Count(message => message.Text.StartsWith("Background task", StringComparison.Ordinal) && message.Text.Contains("completed", StringComparison.Ordinal));
            if (ended < Products.Length)
            {
                return new ChatMessage(ChatRole.Assistant, "The counts are running.");
            }
            if (!calls.Any(call => call.Name == "GetTaskResult"))
            {
                return new ChatMessage(ChatRole.Assistant,
                [
                    .. Enumerable.Range(1, Products.Length).Select(id => new FunctionCallContent($"read{id}", "GetTaskResult", new Dictionary<string, object?> { ["taskId"] = id }))
                ]);
            }

            string[] results = [.. request.SelectMany(message => message.Contents).OfType<FunctionResultContent>()
                .Select(result => result.Result?.ToString() ?? string.Empty).Where(text => text.Contains("tickets", StringComparison.Ordinal))
                .Select(text => text[(text.LastIndexOf('\n') + 1)..])];
            return new ChatMessage(ChatRole.Assistant, string.Join("; ", results));
        }
    }

    [Fact]
    public async Task ThreeCountsRunAtTheSameTime_AndTheAnswerHoldsAllThreeNumbers()
    {
        CountingModel model = new();
        RecordingConsole console = new();
        Func<TimeSpan> realTime = Sample.CountTime;
        // The sample counts for seconds so a person can watch; the test only needs the three to overlap.
        Sample.CountTime = () => TimeSpan.FromMilliseconds(50);

        try
        {
            await Sample.RunAsync(model, console, Token);
        }
        finally
        {
            Sample.CountTime = realTime;
        }

        // The sample writes no answer itself: the console draws it from the events, once.
        Assert.Equal("Atlas: 15 tickets; Borealis: 24 tickets; Cirrus: 18 tickets", console.Events.OfType<AssistantTextCompleted>().Last().Text);
        Assert.DoesNotContain("tickets", console.Output);
        Assert.Equal([1, 2, 3], console.Events.OfType<BackgroundTaskStarted>().Select(task => task.TaskId).Order());
        Assert.Equal(3, console.Events.OfType<BackgroundTaskEnded>().Count());
        // Each start answered at once with a task id, before any count had finished.
        string[] starts = [.. model.Requests[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.Result?.ToString() ?? string.Empty)];
        Assert.Equal(3, starts.Count(text => text.StartsWith("Started task", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile(Sample.RunAsync);
}
