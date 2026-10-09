using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;
using PinkRooster.Agents;
using Spectre.Console.Testing;

namespace PinkRooster.SpectreConsole.Tests;

public sealed class AgentConsoleExtensionsTests
{
    [AgentRole("You answer.")]
    private sealed class Plain(IChatClient chatClient) : DeclaredAgent(chatClient);

    private sealed class FixedReply(string text) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    // Asks for one tool call, then answers once the call's result is in the request.
    private sealed class NeedsApproval : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(Next(messages)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (ChatResponseUpdate update in new ChatResponse(Next(messages)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        private static ChatMessage Next(IEnumerable<ChatMessage> messages) =>
            messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Any()
                ? new ChatMessage(ChatRole.Assistant, "Restarted.")
                : new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Restart", new Dictionary<string, object?>())]);
    }

    private sealed class Approver : IAgentConsole
    {
        public List<string> Asked { get; } = [];

        public bool IsInteractive => true;

        public void WriteLine(string text = "") { }

        public void WriteAnswer(string text) { }

        public void Write(string text) { }

        public void WriteEvent(AgentEvent item) { }

        public Task<ApprovalChoice> ConfirmToolCallAsync(ApprovalQuestion question, CancellationToken cancellationToken)
        {
            Asked.Add(question.Call.Name);
            return Task.FromResult(ApprovalChoice.Allow);
        }

        public Task<IReadOnlyList<UserQuestionAnswer>> AskAsync(IReadOnlyList<UserQuestion> questions, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task RunToConsole_WithAPolicy_AsksThroughTheConsoleOnce_AndTheSessionWideAnswerCoversTheNextRun()
    {
        int ran = 0;
        AIAgent agent = new NeedsApproval().CreateAgent().WithRole("You answer.")
            .WithTool(() => { ran++; return "ok"; }, "Restart", "Restarts it.")
            .RequireApproval("Restart")
            .Build();
        TestConsole terminal = new TestConsole().Interactive();
        terminal.Input.PushKey(ConsoleKey.DownArrow);
        terminal.Input.PushKey(ConsoleKey.Enter);
        PermissionPolicy policy = new(new AgentConsole(terminal).ConfirmToolCallAsync);

        var first = await agent.RunToConsoleAsync("Restart it", policy, cancellationToken: TestContext.Current.CancellationToken);
        // No key is left to press: a second question would hang the test's console.
        var second = await agent.RunToConsoleAsync("Restart it again", policy, cancellationToken: TestContext.Current.CancellationToken);

        Assert.EndsWith("Restarted.", first.Text);
        Assert.EndsWith("Restarted.", second.Text);
        Assert.Equal(2, ran);
        Assert.Equal("every Restart call", Assert.Single(policy.SessionRules).ToString());
    }

    [Fact]
    public async Task WithConsole_ThenRunToConsole_DrawsTheAnswer_AndReturnsTheResponse()
    {
        TestConsole console = new();
        await using Plain agent = new FixedReply("The answer is 42.").CreateAgent<Plain>().WithConsole(console);

        var response = await agent.RunToConsoleAsync("What is it?", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("The answer is 42.", response.Text);
        Assert.Contains("The answer is 42.", console.Output);
    }

    [Fact]
    public async Task RunToConsole_WithoutAConsole_ReturnsTheResponse_AndDrawsNothing()
    {
        await using Plain agent = new FixedReply("quiet").CreateAgent<Plain>();

        var response = await agent.RunToConsoleAsync("Hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("quiet", response.Text);
    }

    [Fact]
    public async Task WithConsole_OnTheBuilder_DrawsTheRunsOfTheAgentItBuilds()
    {
        TestConsole terminal = new();
        AgentConsole console = new(terminal);
        AIAgent agent = new FixedReply("Built and drawn.").CreateAgent().WithRole("You answer.").WithConsole(console).Build();

        var response = await agent.RunToConsoleAsync("Hi", console, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Built and drawn.", response.Text);
        Assert.Contains("Built and drawn.", terminal.Output);
    }

    [Fact]
    public async Task RunToConsole_WithAConsole_AsksItAboutACallThatNeedsApproval_AndContinues()
    {
        int ran = 0;
        AIAgent agent = new NeedsApproval().CreateAgent().WithRole("You answer.")
            .WithTool(() => { ran++; return "ok"; }, "Restart", "Restarts it.")
            .RequireApproval("Restart")
            .Build();
        Approver console = new();

        var response = await agent.RunToConsoleAsync("Restart it", console, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Restart"], console.Asked);
        Assert.Equal(1, ran);
        Assert.EndsWith("Restarted.", response.Text);
    }

    [Fact]
    public void WithConsole_ReturnsTheSameAgent()
    {
        Plain agent = new FixedReply("x").CreateAgent<Plain>();

        Assert.Same(agent, agent.WithConsole(new TestConsole()));
    }
}
