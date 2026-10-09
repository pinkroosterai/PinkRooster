using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Background;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Steps;

namespace PinkRooster.Agents.Tests.Background;

/// <summary>The inbox and the wake: what the model is told while it runs, and what keeps a run going.</summary>
public sealed class BackgroundInboxTests
{
    private static readonly Dictionary<string, object?> InBackground = new() { ["runInBackground"] = true };

    private static ChatMessage Call(string name, Dictionary<string, object?>? arguments = null) =>
        new(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), name, arguments ?? [])]);

    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    private static string Answer(ScriptedChatClient model, int request) =>
        model.Requests[request].SelectMany(message => message.Contents).OfType<FunctionResultContent>().Last().Result?.ToString() ?? string.Empty;

    private static string[] Updates(ScriptedChatClient model, int request) =>
        [.. model.Requests[request].Where(message => message.AuthorName == BackgroundTaskStore.UpdateAuthor).Select(message => message.Text)];

    private static int Count(ScriptedChatClient model, int request, string text) =>
        model.Requests[request].Count(message => message.Text == text);

    // A tool that runs until it is released; Entered completes when a call has begun.
    private sealed class Gate
    {
        private readonly TaskCompletionSource<string> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AIFunction Tool(string name = "Slow") => AIFunctionFactory.Create(async (CancellationToken cancellationToken) =>
        {
            entered.TrySetResult();
            return await release.Task.WaitAsync(cancellationToken);
        }, name);

        public Task Entered => entered.Task;

        public void Release(string result) => release.TrySetResult(result);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, limit.Token);
        }
    }

    [Fact]
    public async Task ModelThatEndsItsTurnWithATaskRunning_IsNotCalledWhileItWaits_AndOnceWhenTheTaskEnds()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Text("started it"), Call("GetTaskResult", new() { ["taskId"] = 1 }), Text("done"));
        System.Collections.Concurrent.ConcurrentQueue<AgentEvent> events = [];
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).AllowBackground("Slow").OnEvent(events.Enqueue).Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task<AgentResponse> run = agent.RunAsync("go", session);
        // The run says when it starts to wait; from then on only the task's ending makes a model call.
        await WaitUntil(() => events.OfType<BackgroundWaitStarted>().Any());
        int callsWhileWaiting = model.Requests.Count;
        gate.Release("slow result");
        AgentResponse response = await run;

        Assert.Equal(2, callsWhileWaiting);
        Assert.Equal([1], Assert.Single(events.OfType<BackgroundWaitStarted>()).TaskIds);
        Assert.Equal(4, model.Requests.Count);
        Assert.Equal(["Background task 1 (Slow) completed. Read its result with GetTaskResult."], Updates(model, 2));
        Assert.Equal("Task 1 (Slow) completed. Its result:\nslow result", Answer(model, 3));
        Assert.Equal("done", response.Text);
    }

    [Fact]
    public async Task TaskThatEndsWhileWaitForTasksWaitsOnIt_IsToldByTheWaitOnly()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Call("WaitForTasks", new() { ["taskIds"] = new[] { 1 } }), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).AllowBackground("Slow").Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task run = agent.RunAsync("go", session);
        await WaitUntil(() => agent.GetService<BackgroundRunAgent>()!.Find(session)?.Find(1) is { Waiters: > 0 });
        gate.Release("slow result");
        await run;

        Assert.Equal("Task 1 (Slow) completed. Its result:\nslow result", Answer(model, 2));
        Assert.Equal(3, model.Requests.Count);
        Assert.All(Enumerable.Range(0, 3), request => Assert.Empty(Updates(model, request)));
    }

    [Fact]
    public async Task MessagePostedDuringAToolCall_IsInTheNextModelCall_AndInTheHistoryOfTheNextRun()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow"), Text("done"), Text("again"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).AllowBackground("Slow").Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task run = agent.RunAsync("go", session);
        await gate.Entered;
        await agent.PostMessageAsync(session, "use the short form");
        gate.Release("slow result");
        await run;
        await agent.RunAsync("and now?", session);

        Assert.Equal(1, Count(model, 1, "use the short form"));
        Assert.Equal(1, Count(model, 2, "use the short form"));
        Assert.Equal(1, Count(model, 2, "go"));
        Assert.Equal(1, Count(model, 2, "done"));
        Assert.Single(model.Requests[2].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
    }

    [Fact]
    public async Task TryPost_IsFalseWithoutARunAndAfterTheRunEnded_AndTrueWhileItCanBeRead()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow"), Text("done"));
        bool? atRunCompleted = null;
        AIAgent? built = null;
        AgentSession? session = null;
        // The run's own end is the last moment a host could still try: the inbox is closed by then.
        built = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).WithInbox()
            .OnEvent(item =>
            {
                if (item is RunCompleted)
                {
                    atRunCompleted = built!.TryPostMessageAsync(session!, "too late").GetAwaiter().GetResult();
                }
            })
            .Build();
        session = await built.CreateSessionAsync();

        Assert.True(built.HasInbox());
        Assert.False(model.CreateAgent().WithRole("r").Build().HasInbox());
        Assert.False(await built.TryPostMessageAsync(session, "nobody runs yet"));

        Task run = built.RunAsync("go", session);
        await gate.Entered;
        Assert.True(await built.TryPostMessageAsync(session, "use the short form"));
        gate.Release("slow result");
        await run;

        Assert.False(atRunCompleted);
        Assert.False(await built.TryPostMessageAsync(session, "the run is over"));
        Assert.Equal(1, Count(model, 1, "use the short form"));
        Assert.Equal(0, model.Requests.Sum(request => request.Count(message => message.Text is "nobody runs yet" or "too late" or "the run is over")));
    }

    [Fact]
    public async Task MessagePostedWhileARunWaitsForAnApproval_IsReadByTheRunThatCarriesTheAnswer()
    {
        ScriptedChatClient model = new(Call("Restart"), Text("restarted"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(AIFunctionFactory.Create(() => "ok", "Restart")).RequireApproval("Restart").WithInbox().Build();
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse asked = await agent.RunAsync("go", session);
        ToolApprovalRequestContent request = Assert.Single(asked.GetApprovalRequests());
        Assert.True(await agent.TryPostMessageAsync(session, "and tell me when it is back"));
        await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);

        Assert.Equal(1, Count(model, 1, "and tell me when it is back"));
        Assert.Single(model.Requests[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
    }

    [Fact]
    public async Task MessagePostedToARunThatIsCancelledBeforeItIsRead_IsReadOnceByTheSessionsNextRun()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow"), Text("after the cancel"), Text("again"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).WithInbox().Build();
        AgentSession session = await agent.CreateSessionAsync();
        using CancellationTokenSource cancel = new();

        Task run = agent.RunAsync("go", session, cancellationToken: cancel.Token);
        await gate.Entered;
        Assert.True(await agent.TryPostMessageAsync(session, "use the short form"));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await agent.RunAsync("carry on", session);
        await agent.RunAsync("and now?", session);

        Assert.Equal(1, Count(model, 1, "use the short form"));
        Assert.Equal(1, Count(model, 2, "use the short form"));
        Assert.Equal(1, Count(model, 2, "carry on"));
    }

    // MAF puts its injecting client above a tool loop the builder supplies, so the message is read when that loop's turn is over.
    [Fact]
    public async Task MessagePostedDuringAToolCall_WithAToolLoopFromConfigureToolLoop_IsReadOneModelCallLater()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow"), Text("done"), Text("done, short"), Text("again"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).AllowBackground("Slow")
            .ConfigureToolLoop(loop => loop.MaximumIterationsPerRequest = 20).Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task<AgentResponse> run = agent.RunAsync("go", session);
        await gate.Entered;
        await agent.PostMessageAsync(session, new ChatMessage(ChatRole.User, "use the short form"));
        gate.Release("slow result");
        AgentResponse response = await run;
        await agent.RunAsync("and now?", session);

        Assert.Equal(0, Count(model, 1, "use the short form"));
        Assert.Equal(1, Count(model, 2, "use the short form"));
        Assert.Equal("done, short", response.Messages.Last().Text);
        Assert.Equal(1, Count(model, 3, "use the short form"));
        Assert.Equal(1, Count(model, 3, "go"));
        Assert.Equal(1, Count(model, 3, "done"));
        Assert.Equal(1, Count(model, 3, "done, short"));
        Assert.Single(model.Requests[3].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
    }

    [Fact]
    public async Task Post_EndsAWaitingWaitForTasks_BeforeItsTimeout()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Call("WaitForTasks", new() { ["taskIds"] = new[] { 1 } }),
            Call("CancelTask", new() { ["taskId"] = 1 }), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).AllowBackground("Slow").Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task run = agent.RunAsync("go", session);
        await WaitUntil(() => agent.GetService<BackgroundRunAgent>()!.Find(session)?.Find(1) is { Waiters: > 0 });
        await agent.PostMessageAsync(session, new ChatMessage(ChatRole.User, "stop that"));
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("A message arrived for you before a listed task ended; the unfinished tasks keep running.\n\nTask 1 (Slow) is still running.", Answer(model, 2));
        Assert.Equal(1, Count(model, 2, "stop that"));
    }

    [Fact]
    public async Task Post_WakesAModelThatEndedItsTurnWhileATaskRuns()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Text("waiting"), Call("CancelTask", new() { ["taskId"] = 1 }), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).AllowBackground("Slow").Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task<AgentResponse> run = agent.RunAsync("go", session);
        await WaitUntil(() => model.Requests.Count == 2);
        await agent.PostMessageAsync(session, new ChatMessage(ChatRole.User, "never mind"));
        AgentResponse response = await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, Count(model, 2, "never mind"));
        Assert.Equal("done", response.Text);
    }

    [Fact]
    public async Task UpdateOfATaskTheWaitDoesNotList_DoesNotEndTheWait()
    {
        Gate first = new();
        Gate second = new();
        ScriptedChatClient model = new(Call("A", InBackground), Call("B", InBackground), Call("WaitForTasks", new() { ["taskIds"] = new[] { 2 } }), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(first.Tool("A")).WithTool(second.Tool("B")).AllowBackground("A", "B").Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task run = agent.RunAsync("go", session);
        BackgroundTaskStore? store = null;
        await WaitUntil(() => (store = agent.GetService<BackgroundRunAgent>()!.Find(session))?.Find(2) is { Waiters: > 0 });
        first.Release("one");
        await store!.Find(1)!.Ended;
        // Task 1 has ended and posted its update; the wait on task 2 goes on.
        Assert.Equal(3, model.Requests.Count);
        Assert.True(store.Find(2)!.Waiters > 0);
        second.Release("two");
        await run;

        Assert.Equal("Task 2 (B) completed. Its result:\ntwo", Answer(model, 3));
        Assert.Equal(["Background task 1 (A) completed. Read its result with GetTaskResult."], Updates(model, 3));
    }

    [Fact]
    public async Task MessageFromTheHost_IsNotCountedAsAWake()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Text("waiting"), Text("still waiting"), Text("done"));
        System.Collections.Concurrent.ConcurrentQueue<AgentEvent> events = [];
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).AllowBackground("Slow")
            .ConfigureBackground(options => options.MaxWakes = 1).OnEvent(events.Enqueue).Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task<AgentResponse> run = agent.RunAsync("go", session);
        await WaitUntil(() => events.OfType<BackgroundWaitStarted>().Count() == 1);
        await agent.PostMessageAsync(session, new ChatMessage(ChatRole.User, "how is it going?"));
        await WaitUntil(() => events.OfType<BackgroundWaitStarted>().Count() == 2);
        gate.Release("slow result");
        AgentResponse response = await run.WaitAsync(TimeSpan.FromSeconds(10));

        // The call for the host's message did not use up the one wake, so the task's ending still calls the model.
        Assert.Equal("done", response.Text);
        Assert.Equal(1, Count(model, 2, "how is it going?"));
        Assert.Empty(events.OfType<BackgroundWakeLimitReached>());
    }

    [Fact]
    public async Task WithInbox_GivesAnAgentWithoutBackgroundToolsAnInbox_AndNoTaskTools()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow"), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(gate.Tool()).WithInbox().Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task run = agent.RunAsync("go", session);
        await gate.Entered;
        await agent.PostMessageAsync(session, new ChatMessage(ChatRole.User, "use the short form"));
        gate.Release("slow result");
        await run;

        Assert.Equal(1, Count(model, 1, "use the short form"));
        Assert.Equal(["Slow"], model.Options[0]!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task AgentWithAReducedHistoryAndABackgroundTool_KeepsToTheReducersLimit_AndSendsNoMessageTwice()
    {
        ScriptedChatClient model = new(Call("Quick", new() { ["text"] = "it" }), Text("first answer"), Text("second answer"), Text("third answer"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(AIFunctionFactory.Create((string text) => $"did {text}", "Quick"))
#pragma warning disable MEAI001 // MEAI's reducer is experimental; it is the one a host would reach for first.
            .WithChatHistoryProvider(new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions { ChatReducer = new MessageCountingChatReducer(4) }))
#pragma warning restore MEAI001
            .AllowBackground("Quick").Build();
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("first question", session);
        await agent.RunAsync("second question", session);
        await agent.RunAsync("third question", session);

        string[] last = [.. model.Requests[3].Select(message => message.Text).Where(text => text.Length > 0)];
        Assert.Equal(last.Distinct(), last);
        Assert.Equal("third question", last[^1]);
        Assert.Contains("second answer", last);
        Assert.True(model.Requests[3].Count <= 5, $"The reducer keeps 4 messages and the run adds 1; the request held {model.Requests[3].Count}.");
    }

    [Fact]
    public async Task Post_WithNoRunInProgress_OrOnAnAgentWithoutBackgroundTools_ThrowsNamingTheFix()
    {
        AIAgent with = new ScriptedChatClient(Text("ok")).CreateAgent().WithRole("r").WithTool(new Gate().Tool()).AllowBackground("Slow").Build();
        AIAgent without = new ScriptedChatClient(Text("ok")).CreateAgent().WithRole("r").Build();
        ChatMessage message = new(ChatRole.User, "hello");

        InvalidOperationException idle = await Assert.ThrowsAsync<InvalidOperationException>(async () => await with.PostMessageAsync(await with.CreateSessionAsync(), message));
        InvalidOperationException none = await Assert.ThrowsAsync<InvalidOperationException>(async () => await without.PostMessageAsync(await without.CreateSessionAsync(), message));

        Assert.Contains("No run is in progress on this session", idle.Message);
        Assert.Contains("Build it with AgentBuilder.WithInbox(), or with AllowBackground", none.Message);
    }

    [Fact]
    public async Task AgentWithAHistoryProviderAndABackgroundTool_SendsEachEarlierMessageOnceInTheNextRun()
    {
        ScriptedChatClient model = new(Call("Quick", new() { ["text"] = "it" }), Text("first answer"), Text("second answer"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(AIFunctionFactory.Create((string text) => $"did {text}", "Quick"))
            .WithChatHistoryProvider(new InMemoryChatHistoryProvider()).AllowBackground("Quick").Build();
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("first question", session);
        await agent.RunAsync("second question", session);

        List<ChatMessage> last = model.Requests[2];
        Assert.Equal(["first question", "", "", "first answer", "second question"], last.Select(message => message.Text));
        Assert.Single(last.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Single(last.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
    }

    [AgentRole("You step.")]
    private sealed class Stepper(IChatClient chatClient, AIFunction slow) : SteppedAgent(chatClient)
    {
        protected override int MaxTurns => 2;

        protected override void Configure(AgentBuilder agent) => agent.WithTool(slow).AllowBackground("Slow");

        protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken) =>
            ValueTask.FromResult(NextStep.Send("second", "Go on."));
    }

    [Fact]
    public async Task StepOfASteppedAgent_WaitsForItsOwnTasks_AndTheWakeIsNotOneOfTheProgramsTurns()
    {
        Gate gate = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Text("step one, started"), Text("step one, finished"), Text("step two"));
        Stepper agent = new(model, gate.Tool());
        AgentSession session = await agent.CreateSessionAsync();

        Task<AgentResponse> run = agent.RunAsync("go", session);
        await WaitUntil(() => model.Requests.Count == 2);
        gate.Release("slow result");
        AgentResponse response = await run.WaitAsync(TimeSpan.FromSeconds(10));

        // Two turns are all MaxTurns allows; the model call the wake made between them is not a turn.
        Assert.Equal(4, model.Requests.Count);
        Assert.Equal("step two", response.Text);
        Assert.Equal(1, Count(model, 3, "Go on."));
    }
}
