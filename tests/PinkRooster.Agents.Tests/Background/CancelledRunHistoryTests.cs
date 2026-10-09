using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Tests.Background;

/// <summary>What a session holds after a run was cancelled while a tool call ran, for an agent whose history is saved after every model call.</summary>
public sealed class CancelledRunHistoryTests
{
    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    private static string[] Unanswered(List<ChatMessage> request)
    {
        HashSet<string> answered = [.. request.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.CallId)];
        return [.. request.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Select(call => call.CallId).Where(id => !answered.Contains(id))];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterARunIsCancelledDuringAToolCall_TheNextRequestHasAnAnswerForEveryToolCall(bool streaming)
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AIFunction slow = AIFunctionFactory.Create(async (CancellationToken cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return "never";
        }, "Slow");
        // Two calls in one reply: the run is cancelled during the first, and the second never starts.
        ChatMessage twoCalls = new(ChatRole.Assistant, [new FunctionCallContent("call-1", "Slow", new Dictionary<string, object?>()), new FunctionCallContent("call-2", "Slow", new Dictionary<string, object?>())]);
        ScriptedChatClient model = new(twoCalls, Text("after the cancel"));
        AIAgent agent = model.CreateAgent().WithoutDefaults().WithRole("r").WithTool(slow).WithInbox().Build();
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        using CancellationTokenSource cancel = new();

        Task run = streaming
            ? agent.RunStreamingAsync("first", session, cancellationToken: cancel.Token).ToAgentResponseAsync(cancel.Token)
            : agent.RunAsync("first", session, cancellationToken: cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        AgentResponse next = await agent.RunAsync("second", session, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("after the cancel", next.Text);
        List<ChatMessage> request = model.Requests[1];
        Assert.Empty(Unanswered(request));
        FunctionResultContent[] results = [.. request.SelectMany(message => message.Contents).OfType<FunctionResultContent>()];
        Assert.Equal(["call-1", "call-2"], results.Select(result => result.CallId));
        Assert.All(results, result => Assert.StartsWith("Cancelled:", result.Result?.ToString()));
        // The answers sit directly after the message that made the calls.
        int calls = request.FindIndex(message => message.Contents.OfType<FunctionCallContent>().Any());
        Assert.Equal(ChatRole.Tool, request[calls + 1].Role);
        Assert.Equal(["first", "second"], request.Where(message => message.Role == ChatRole.User).Select(message => message.Text));
    }
}
