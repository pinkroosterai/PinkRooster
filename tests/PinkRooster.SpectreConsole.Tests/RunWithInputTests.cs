using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.SpectreConsole.Tests.TestSupport;
using Spectre.Console.Testing;

namespace PinkRooster.SpectreConsole.Tests;

/// <summary>Typing during a run: scripted keys on a test console, a scripted model, and what the model was sent.</summary>
public sealed class RunWithInputTests
{
    private static ChatMessage Call(string name) => new(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), name, new Dictionary<string, object?>())]);

    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    private static int Count(ScriptedChatClient model, int request, string text) => model.Requests[request].Count(message => message.Text == text);

    private static int CountInAll(ScriptedChatClient model, string text) => model.Requests.Sum(request => request.Count(message => message.Text == text));

    private static string[] Unanswered(List<ChatMessage> request)
    {
        HashSet<string> answered = [.. request.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.CallId)];
        return [.. request.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Select(call => call.CallId).Where(id => !answered.Contains(id))];
    }

    // A tool that runs until it is released or cancelled; Entered completes when a call has begun.
    private sealed class Gate
    {
        private readonly TaskCompletionSource<string> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AIFunction Tool(string name = "Slow") => AIFunctionFactory.Create(async (CancellationToken cancellationToken) =>
        {
            entered.TrySetResult();
            return await release.Task.WaitAsync(cancellationToken);
        }, name);

        public Task Entered => entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Release(string result = "slow result") => release.TrySetResult(result);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            await Task.Delay(10, limit.Token);
        }
    }

    // The inbox has taken this many of the typed lines and the model has not read them yet.
    private static Task InboxHoldsAsync(AIAgent agent, AgentSession session, int count) =>
        WaitUntilAsync(async () => (await agent.GetService<MessageInjectingChatClient>()!.GetPendingMessagesAsync(session)).Count == count);

    private static AIAgent Agent(ScriptedChatClient model, AgentConsole console, Gate gate) =>
        model.CreateAgent().WithoutDefaults().WithRole("r").WithTool(gate.Tool()).WithInbox().WithConsole(console).Build();

    [Fact]
    public async Task ALineSubmittedDuringARun_IsOnceInTheNextRequest_AndOnceInEveryRequestAfterIt()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow"), Text("done"), Text("again"));
        AIAgent agent = Agent(model, console, gate);
        AgentSession session = await agent.CreateSessionAsync(none);

        Task<ConsoleRunResult> run = agent.RunWithInputAsync("go", console, session, cancellationToken: none);
        await gate.Entered;
        terminal.Keys.Submit("use the shrt");
        await InboxHoldsAsync(agent, session, 1);
        gate.Release();
        ConsoleRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(10), none);
        await agent.RunAsync("and now?", session, cancellationToken: none);

        Assert.False(result.Cancelled);
        Assert.Empty(result.UnreadLines);
        Assert.Equal(string.Empty, result.Draft);
        Assert.Same(session, result.Session);
        Assert.EndsWith("done", result.Response.Text);
        Assert.Equal(0, Count(model, 0, "use the shrt"));
        Assert.Equal(1, Count(model, 1, "use the shrt"));
        // The third request is the session's history and the new prompt: the line is in the history once.
        Assert.Equal(1, Count(model, 2, "use the shrt"));
        Assert.Equal(1, Count(model, 2, "go"));
        Assert.Contains("queued: use the shrt", terminal.Output);
    }

    [Fact]
    public async Task Backspace_CorrectsTheLine_AndWhatWasNotSubmittedComesBackAsTheDraft()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow"), Text("done"));
        AIAgent agent = Agent(model, console, gate);
        AgentSession session = await agent.CreateSessionAsync(none);

        Task<ConsoleRunResult> run = agent.RunWithInputAsync("go", console, session, cancellationToken: none);
        await gate.Entered;
        terminal.Keys.Type("shorx");
        terminal.Keys.Press(ConsoleKey.Backspace);
        terminal.Keys.Submit("t form");
        await InboxHoldsAsync(agent, session, 1);
        terminal.Keys.Type("half a thou");
        await WaitUntilAsync(() => Task.FromResult(!terminal.Keys.IsKeyAvailable()));
        gate.Release();
        ConsoleRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(10), none);

        Assert.Equal(1, Count(model, 1, "short form"));
        Assert.Equal("half a thou", result.Draft);
        Assert.Empty(result.UnreadLines);
    }

    [Fact]
    public async Task EscDuringAToolCall_EndsTheRun_AndTheNextRequestHasAnAnswerForEveryToolCall()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow"), Text("picked up again"));
        AIAgent agent = Agent(model, console, gate);
        AgentSession session = await agent.CreateSessionAsync(none);
        using CancellationTokenSource callers = CancellationTokenSource.CreateLinkedTokenSource(none);

        Task<ConsoleRunResult> run = agent.RunWithInputAsync("go", console, session, cancellationToken: callers.Token);
        await gate.Entered;
        terminal.Keys.Press(ConsoleKey.Escape);
        ConsoleRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(10), none);

        Assert.True(result.Cancelled);
        // Esc ended the run, not the caller's own cancellation.
        Assert.False(callers.IsCancellationRequested);
        Assert.Same(session, result.Session);
        Assert.Single(model.Requests);

        AgentResponse next = await agent.RunAsync("carry on", session, cancellationToken: none);

        Assert.Equal("picked up again", next.Text);
        Assert.Empty(Unanswered(model.Requests[1]));
        Assert.Single(model.Requests[1].SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Contains("Esc: stopping the run.", terminal.Output);
    }

    [Fact]
    public async Task ALineSubmittedAfterTheLastModelCall_ComesBackAsTheNextPrompt_AndIsInNoRequest()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        ScriptedChatClient model = new(Text("done"));
        // The run's own end is after its last model call, and before the console stops reading keys.
        AIAgent agent = model.CreateAgent().WithoutDefaults().WithRole("r").WithInbox().WithConsole(console)
            .OnEvent(item =>
            {
                if (item is RunCompleted)
                {
                    terminal.Keys.Submit("one more thing");
                }
            })
            .Build();
        AgentSession session = await agent.CreateSessionAsync(none);

        ConsoleRunResult result = await agent.RunWithInputAsync("go", console, session, cancellationToken: none).WaitAsync(TimeSpan.FromSeconds(10), none);

        Assert.Equal(["one more thing"], result.UnreadLines);
        Assert.Single(model.Requests);
        Assert.Equal(0, CountInAll(model, "one more thing"));
        Assert.Empty(await agent.GetService<MessageInjectingChatClient>()!.GetPendingMessagesAsync(session, none));
    }

    [Fact]
    public async Task ALineTheHostHolds_IsInNoRequest_AndComesBackAfterTheRun_InTheOrderTyped()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow"), Text("done"));
        AIAgent agent = Agent(model, console, gate);
        AgentSession session = await agent.CreateSessionAsync(none);
        ConsoleRunOptions options = new() { HoldLine = line => line.StartsWith('/') };

        Task<ConsoleRunResult> run = agent.RunWithInputAsync("go", console, session, options, none);
        await gate.Entered;
        terminal.Keys.Submit("/undo");
        terminal.Keys.Submit("and keep it short");
        terminal.Keys.Submit("/status");
        await InboxHoldsAsync(agent, session, 1);
        gate.Release();
        ConsoleRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(10), none);

        Assert.Equal(["/undo", "/status"], result.UnreadLines);
        Assert.Equal(1, Count(model, 1, "and keep it short"));
        Assert.Equal(0, CountInAll(model, "/undo"));
        Assert.Equal(0, CountInAll(model, "/status"));
        Assert.Contains("held until the run ends: /undo", terminal.Output);
    }

    [Fact]
    public async Task WhileAnApprovalIsAsked_TheKeyboardBelongsToThePrompt()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        int restarts = 0;
        ScriptedChatClient model = new(Call("Restart"), Text("restarted"));
        AIAgent agent = model.CreateAgent().WithoutDefaults().WithRole("r")
            .WithTool(() => { restarts++; return "ok"; }, "Restart", "Restarts it.")
            .RequireApproval("Restart").WithInbox().WithConsole(console).Build();
        AgentSession session = await agent.CreateSessionAsync(none);

        Task<ConsoleRunResult> run = agent.RunWithInputAsync("go", console, session, cancellationToken: none);
        await terminal.Keys.PromptWaits.WaitAsync(TimeSpan.FromSeconds(10), none);
        // Down and Enter pick Skip in the prompt; had the input line's reader taken them, the prompt would wait for ever.
        terminal.Keys.Press(ConsoleKey.DownArrow);
        terminal.Keys.Press(ConsoleKey.Enter);
        ConsoleRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(10), none);

        Assert.Equal(0, restarts);
        Assert.EndsWith("restarted", result.Response.Text);
        Assert.Empty(result.UnreadLines);
        Assert.Equal(string.Empty, result.Draft);
        Assert.Contains("Skipped.", terminal.Output);
    }

    [Fact]
    public async Task ALineBeingTypedWhenAnApprovalComesUp_StaysALine_AndItsEnterIsNotTakenAsTheAnswer()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        int restarts = 0;
        ScriptedChatClient model = new(Call("Restart"), Text("not restarted"), Text("again"));
        // The first run of the loop ends with the approval request; the keys arrive just before the prompt would open.
        AIAgent agent = model.CreateAgent().WithoutDefaults().WithRole("r")
            .WithTool(() => { restarts++; return "ok"; }, "Restart", "Restarts it.")
            .RequireApproval("Restart").WithInbox().WithConsole(console)
            .OnEvent(item =>
            {
                if (item is RunCompleted { Outcome: RunOutcome.AwaitingApproval })
                {
                    terminal.Keys.Submit("do not restart it");
                }
            })
            .Build();
        AgentSession session = await agent.CreateSessionAsync(none);

        Task<ConsoleRunResult> run = agent.RunWithInputAsync("go", console, session, cancellationToken: none);
        await terminal.Keys.PromptWaits.WaitAsync(TimeSpan.FromSeconds(10), none);
        terminal.Keys.Press(ConsoleKey.DownArrow);
        terminal.Keys.Press(ConsoleKey.Enter);
        ConsoleRunResult result = await run.WaitAsync(TimeSpan.FromSeconds(10), none);

        // Had the prompt taken the typed line, its Enter would have picked Allow.
        Assert.Equal(0, restarts);
        Assert.Contains("queued: do not restart it", terminal.Output);
        Assert.Empty(result.UnreadLines);
        // It was posted while the run waited for the answer, and the run that carried the answer read it.
        Assert.Equal(1, Count(model, 1, "do not restart it"));
    }

    [Fact]
    public async Task WithAPolicy_TheRunsApprovalsAreAnsweredByIt()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        int restarts = 0;
        ScriptedChatClient model = new(Call("Restart"), Text("restarted"));
        AIAgent agent = model.CreateAgent().WithoutDefaults().WithRole("r")
            .WithTool(() => { restarts++; return "ok"; }, "Restart", "Restarts it.")
            .RequireApproval("Restart").WithInbox().WithConsole(console).Build();
        PinkRooster.Agents.Permissions.PermissionPolicy policy = new PinkRooster.Agents.Permissions.PermissionPolicy(console.ConfirmToolCallAsync)
            .Allow(PinkRooster.Agents.Permissions.AllowRule.ForTool("Restart"));

        ConsoleRunResult result = await agent.RunWithInputAsync("go", console, options: new ConsoleRunOptions { Policy = policy }, cancellationToken: none).WaitAsync(TimeSpan.FromSeconds(10), none);

        Assert.Equal(1, restarts);
        Assert.NotNull(result.Session);
        Assert.DoesNotContain("Allow this tool call?", terminal.Output);
    }

    [Fact]
    public async Task AnAgentWithoutAnInbox_ThrowsNamingTheFix_WhenTheConsoleCanTakeInput()
    {
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        AIAgent agent = new ScriptedChatClient(Text("done")).CreateAgent().WithoutDefaults().WithRole("r").WithConsole(console).Build();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunWithInputAsync("go", console, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("WithInbox()", error.Message);
        Assert.Contains("RunToConsoleAsync", error.Message);
    }

    [Fact]
    public async Task OnATerminalThatIsNotInteractive_NothingChanges_AndTheAgentNeedsNoInbox()
    {
        TestConsole terminal = new();
        AgentConsole console = new(terminal);
        ScriptedChatClient model = new(Text("done"));
        AIAgent agent = model.CreateAgent().WithoutDefaults().WithRole("r").WithConsole(console).Build();

        ConsoleRunResult result = await agent.RunWithInputAsync("go", console, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("done", result.Response.Text);
        Assert.False(result.Cancelled);
        Assert.Empty(result.UnreadLines);
        Assert.Contains("done", terminal.Output);
        Assert.DoesNotContain("›", terminal.Output);
    }

    [Fact]
    public async Task TheCallersOwnCancellation_ThrowsAsUsual()
    {
        CancellationToken none = TestContext.Current.CancellationToken;
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        Gate gate = new();
        AIAgent agent = Agent(new ScriptedChatClient(Call("Slow"), Text("done")), console, gate);
        using CancellationTokenSource callers = CancellationTokenSource.CreateLinkedTokenSource(none);

        Task<ConsoleRunResult> run = agent.RunWithInputAsync("go", console, cancellationToken: callers.Token);
        await gate.Entered;
        callers.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10), none));
    }
}
