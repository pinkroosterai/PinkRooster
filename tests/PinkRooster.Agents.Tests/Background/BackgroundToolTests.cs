using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Background;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.SubAgents;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.Background;

public sealed class BackgroundToolTests
{
    private static readonly Dictionary<string, object?> InBackground = new() { ["runInBackground"] = true };

    private static ChatMessage Call(string name, Dictionary<string, object?>? arguments = null) =>
        new(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), name, arguments ?? [])]);

    private static ChatMessage Wait(params int[] ids) => Call("WaitForTasks", new() { ["taskIds"] = ids });

    private static ChatMessage Result(int id) => Call("GetTaskResult", new() { ["taskId"] = id });

    private static ChatMessage Cancel(int id) => Call("CancelTask", new() { ["taskId"] = id });

    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    // What the tool call answered in request <paramref name="request"/>, as the model reads it.
    private static string Answer(ScriptedChatClient model, int request) =>
        model.Requests[request].SelectMany(message => message.Contents).OfType<FunctionResultContent>().Last().Result switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()!,
            object other => other.ToString()!,
            null => string.Empty
        };

    private static AIFunction Quick(string name = "Quick") => AIFunctionFactory.Create((string text) => $"did {text}", name);

    // A tool that runs until it is released or cancelled; Cancelled completes when its token was cancelled.
    private sealed class Held
    {
        private readonly TaskCompletionSource<string> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AIFunction Tool(string name = "Slow") => AIFunctionFactory.Create(async (CancellationToken cancellationToken) =>
        {
            try
            {
                return await release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        }, name);

        public Task Cancelled => cancelled.Task;

        public void Release(string result) => release.TrySetResult(result);
    }

    private sealed class Deploys : ToolCollection
    {
        [Tool("Deploy", "Deploys the build.", RequiresApproval = true, Kind = ToolKind.Execute)]
        public string Deploy() => "deployed";
    }

    [Fact]
    public async Task OptedInTool_KeepsItsKind_AlsoWhenItNeedsApproval()
    {
        ScriptedChatClient model = new(Text("ok"));

        await model.CreateAgent().WithRole("r").WithTools(new Deploys()).AllowBackground("Deploy").Build().RunAsync("go");

        AIFunction deploy = model.Options[0]!.Tools!.OfType<AIFunction>().Single(tool => tool.Name == "Deploy");
        // What reaches the model is MAF's own wrapper around the approval wrapper around the background wrapper.
        Assert.NotNull(deploy.GetService<ApprovalRequiredAIFunction>());
        Assert.True(deploy.JsonSchema.GetProperty("properties").TryGetProperty("runInBackground", out _));
        Assert.Equal(ToolKind.Execute, deploy.GetKind());
    }

    [Fact]
    public async Task OptedInTool_CalledWithoutRunInBackground_AnswersWhatTheToolAnswersWithoutTheOptIn()
    {
        ScriptedChatClient plain = new(Call("Quick", new() { ["text"] = "it" }), Text("ok"));
        ScriptedChatClient allowed = new(Call("Quick", new() { ["text"] = "it" }), Call("Quick", new() { ["text"] = "it", ["runInBackground"] = false }), Text("ok"));

        await plain.CreateAgent().WithRole("r").WithTool(Quick()).Build().RunAsync("go");
        await allowed.CreateAgent().WithRole("r").WithTool(Quick()).AllowBackground("quick").Build().RunAsync("go");

        Assert.Equal("did it", Answer(plain, 1));
        Assert.Equal("did it", Answer(allowed, 1));
        Assert.Equal("did it", Answer(allowed, 2));
    }

    [Fact]
    public async Task AllowBackground_AddsTheParameterToTheNamedToolOnly_AndGivesTheAgentTheTaskTools()
    {
        ScriptedChatClient model = new(Text("ok"));

        await model.CreateAgent().WithRole("r").WithTool(Quick()).WithTool(Quick("Other")).AllowBackground("Quick").Build().RunAsync("go");

        Dictionary<string, AIFunction> tools = model.Options.Single()!.Tools!.OfType<AIFunction>().ToDictionary(tool => tool.Name);
        Assert.Equal(["CancelTask", "GetTaskResult", "ListTasks", "Other", "Quick", "WaitForTasks"], tools.Keys.Order());
        JsonElement quick = tools["Quick"].JsonSchema.GetProperty("properties");
        Assert.Equal("boolean", quick.GetProperty("runInBackground").GetProperty("type").GetString());
        Assert.True(quick.TryGetProperty("text", out _));
        Assert.DoesNotContain("runInBackground", tools["Quick"].JsonSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.False(tools["Other"].JsonSchema.GetProperty("properties").TryGetProperty("runInBackground", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolStartedInBackground_ReturnsATaskIdAtOnce_AndItsResultIsReadLater(bool streaming)
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Wait(1), Result(1), Text("done"));
        List<AgentEvent> events = [];
        object gate = new();
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(held.Tool()).AllowBackground("Slow")
            .OnEvent(item => { lock (gate) { events.Add(item); } }).Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task run = streaming ? Drain(agent.RunStreamingAsync("go", session)) : agent.RunAsync("go", session);
        // The tool is released once WaitForTasks waits on it, so the wait is what reports its ending.
        await WaitUntil(() => agent.GetService<BackgroundRunAgent>()!.Find(session)?.Find(1) is { Waiters: > 0 });
        string started = Answer(model, 1);
        held.Release("slow result");
        await run;

        Assert.Equal("Started task 1 (Slow). WaitForTasks returns its result once it has ended.", started);
        Assert.Equal("Task 1 (Slow) completed. Its result:\nslow result", Answer(model, 2));
        Assert.Equal("Task 1 (Slow) completed. Its result:\nslow result", Answer(model, 3));
        BackgroundTaskStarted start = Assert.Single(events.OfType<BackgroundTaskStarted>());
        BackgroundTaskEnded end = Assert.Single(events.OfType<BackgroundTaskEnded>());
        Assert.Equal((1, "Slow", 1, "Slow", BackgroundTaskState.Completed, false), (start.TaskId, start.ToolName, end.TaskId, end.ToolName, end.State, end.DidNotStop));
        Assert.Equal(start.RunId, end.RunId);
    }

    [Theory]
    [InlineData(false, "Task 1 (Boom) failed. It has no result.")]
    [InlineData(true, "Task 1 (Boom) failed: no disk")]
    public async Task ToolThatThrows_EndsAsFailed_IsLogged_AndShowsItsMessageOnlyWhenTheToolLoopShowsErrorDetails(bool detailed, string expected)
    {
        ScriptedChatClient model = new(Call("Boom", InBackground), Wait(1), Result(1), Text("done"));
        TaskCompletionSource fail = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AIFunction boom = AIFunctionFactory.Create(async Task<string> () =>
        {
            await fail.Task;
            throw new InvalidOperationException("no disk");
        }, "Boom");
        RecordingLoggerFactory loggers = new();
        ConcurrentQueue<AgentEvent> events = [];
        AgentBuilder builder = model.CreateAgent().WithRole("r").WithTool(boom).AllowBackground("Boom").WithLoggerFactory(loggers).OnEvent(events.Enqueue);
        if (detailed)
        {
            builder.ConfigureToolLoop(loop => loop.IncludeDetailedErrors = true);
        }
        AIAgent agent = builder.Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task run = agent.RunAsync("go", session);
        // The tool fails once WaitForTasks waits on it, so the wait is what reports the failure.
        await WaitUntil(() => agent.GetService<BackgroundRunAgent>()!.Find(session)?.Find(1) is { Waiters: > 0 });
        fail.SetResult();
        await run;

        Assert.Equal(expected, Answer(model, 2));
        Assert.Equal(expected, Answer(model, 3));
        BackgroundTaskEnded end = Assert.Single(events.OfType<BackgroundTaskEnded>());
        Assert.Equal((BackgroundTaskState.Failed, "no disk"), (end.State, end.Error?.Message));
        Assert.Contains(loggers.Logger.Entries, entry => entry.Message.Contains("Background task 1 (Boom) failed.") && entry.Exception?.Message == "no disk");
    }

    [Fact]
    public async Task TaskPastItsTimeLimit_IsStopped_AndEndsAsTimedOut()
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Wait(1), Result(1), Text("done"));

        await model.CreateAgent().WithRole("r").WithTool(held.Tool()).AllowBackground("Slow")
            .ConfigureBackground(options => options.TaskTimeLimit = TimeSpan.FromMilliseconds(50)).Build().RunAsync("go");

        await held.Cancelled;
        Assert.Equal("Task 1 (Slow) timed out after 50 milliseconds and was stopped. It has no result.", Answer(model, 2));
        Assert.Equal(Answer(model, 2), Answer(model, 3));
    }

    [Fact]
    public async Task CancelTask_StopsARunningTask_AndLeavesAnEndedOneAsItIs()
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Result(1), Call("CancelTask", new() { ["taskId"] = 1 }),
            Call("CancelTask", new() { ["taskId"] = 1 }), Result(1), Call("ListTasks"), Text("done"));

        await model.CreateAgent().WithRole("r").WithTool(held.Tool()).AllowBackground("Slow").Build().RunAsync("go");

        await held.Cancelled;
        Assert.Equal("Task 1 (Slow) is still running.", Answer(model, 2));
        Assert.Equal("task 1 (Slow): cancelled", Answer(model, 3));
        Assert.Equal("Nothing to cancel: task 1 (Slow): cancelled.", Answer(model, 4));
        Assert.Equal("Task 1 (Slow) was cancelled. It has no result.", Answer(model, 5));
        Assert.StartsWith("task 1 (Slow): cancelled, ", Answer(model, 6));
    }

    [Fact]
    public async Task StartOverTheLimit_StartsNothing_AndNamesTheLimit()
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Call("Slow", InBackground), Call("ListTasks"), Cancel(1), Text("done"));

        await model.CreateAgent().WithRole("r").WithTool(held.Tool()).AllowBackground("Slow")
            .ConfigureBackground(options => options.MaxRunningTasks = 1).Build().RunAsync("go");

        Assert.StartsWith("Error: 1 background tasks are running, and at most 1 may run at once. Nothing was started.", Answer(model, 2));
        Assert.DoesNotContain("task 2", Answer(model, 3));
    }

    [Fact]
    public async Task UnknownTaskId_ReturnsAnErrorThatListsTheIdsThereAre()
    {
        ScriptedChatClient model = new(Result(7), Call("Quick", new() { ["text"] = "a", ["runInBackground"] = true }), Wait(1, 9),
            Cancel(9), Call("WaitForTasks", new() { ["taskIds"] = Array.Empty<int>() }), Wait(1), Text("done"));

        await model.CreateAgent().WithRole("r").WithTool(Quick()).AllowBackground("Quick").Build().RunAsync("go");

        Assert.Equal("Error: No task 7. There are no background tasks in this run.", Answer(model, 1));
        Assert.Equal("Error: No task 9. The tasks of this run are: 1.", Answer(model, 3));
        Assert.Equal("Error: No task 9. The tasks of this run are: 1.", Answer(model, 4));
        Assert.StartsWith("Error: taskIds is empty.", Answer(model, 5));
    }

    [Fact]
    public async Task WaitForTasks_ThatTimesOut_SaysSoAndLeavesTheTaskRunning()
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Wait(1), Cancel(1), Text("done"));

        await model.CreateAgent().WithRole("r").WithTool(held.Tool()).AllowBackground("Slow")
            .ConfigureBackground(options => options.DefaultWaitTimeout = TimeSpan.FromMilliseconds(50)).Build().RunAsync("go");

        Assert.Equal("50 milliseconds passed before a listed task ended; the unfinished tasks keep running.\n\nTask 1 (Slow) is still running.", Answer(model, 2));
    }

    [Fact]
    public async Task ResultThatIsNotText_IsReadAsJson_AndALongOneIsCutInTheMiddle()
    {
        ScriptedChatClient model = new(Call("Pair", InBackground), Call("Long", InBackground), Call("Sync"), Result(1), Result(2), Text("done"));
        AIFunction pair = AIFunctionFactory.Create(() => new { Name = "a", Count = 2 }, "Pair");
        AIFunction longText = AIFunctionFactory.Create(() => new string('x', 500), "Long");
        // Returns once both tasks have ended, so the script reads results that are there.
        AIFunction sync = AIFunctionFactory.Create(async () =>
        {
            BackgroundTaskStore store = BackgroundTaskStore.Current!;
            await WaitUntil(() => store.All().All(task => task.Ended.IsCompleted));
            return "both ended";
        }, "Sync");

        await model.CreateAgent().WithRole("r").WithTool(pair).WithTool(longText).WithTool(sync).AllowBackground("Pair", "Long")
            .ConfigureBackground(options => options.MaxResultCharacters = 100).Build().RunAsync("go");

        Assert.Contains("\"count\":2", Answer(model, 4).Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("characters omitted", Answer(model, 5));
        Assert.True(Answer(model, 5).Length < 200);
    }

    [Fact]
    public async Task RunAtItsLimitOnWakes_CancelsItsRunningTasks_AndReturnsTheLastResponse()
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Text("started it"));
        ConcurrentQueue<AgentEvent> events = [];
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(held.Tool()).AllowBackground("Slow")
            .ConfigureBackground(options => options.MaxWakes = 0).OnEvent(events.Enqueue).Build();

        AgentResponse response = await agent.RunAsync("go");

        Assert.Equal("started it", response.Text);
        Assert.True(held.Cancelled.IsCompleted);
        BackgroundTaskEnded end = Assert.Single(events.OfType<BackgroundTaskEnded>());
        Assert.Equal((BackgroundTaskState.Cancelled, false), (end.State, end.DidNotStop));
        Assert.Empty(events.OfType<BackgroundWakeLimitReached>());
    }

    [Fact]
    public async Task CancelledRun_LeavesNoTaskRunning()
    {
        Held held = new();
        using CancellationTokenSource stop = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Call("Stop"));
        AIFunction stopTool = AIFunctionFactory.Create(() => { stop.Cancel(); return "stopped"; }, "Stop");
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(held.Tool()).WithTool(stopTool).AllowBackground("Slow").Build();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agent.RunAsync("go", cancellationToken: stop.Token));

        Assert.True(held.Cancelled.IsCompleted);
    }

    [Fact]
    public async Task ToolThatIgnoresCancellation_IsReportedAsNotStopped_AfterTheGracePeriod()
    {
        TaskCompletionSource<string> never = new();
        AIFunction stubborn = AIFunctionFactory.Create(() => never.Task, "Stubborn");
        ScriptedChatClient model = new(Call("Stubborn", InBackground), Cancel(1), Result(1), Text("done"));
        ConcurrentQueue<AgentEvent> events = [];
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(stubborn).AllowBackground("Stubborn")
            .ConfigureBackground(options => options.GracePeriod = TimeSpan.FromMilliseconds(50)).OnEvent(events.Enqueue).Build();

        await agent.RunAsync("go").WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("task 1 (Stubborn): cancelled, but it did not stop", Answer(model, 2));
        Assert.Equal("Task 1 (Stubborn) was cancelled, but its tool did not stop and may still be running. It has no result.", Answer(model, 3));
        BackgroundTaskEnded end = Assert.Single(events.OfType<BackgroundTaskEnded>());
        Assert.Equal((BackgroundTaskState.Cancelled, true), (end.State, end.DidNotStop));
    }

    [Fact]
    public async Task ApprovalPause_KeepsTheTaskRunning_AndTheRunWithTheAnswersReadsItsResult()
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Call("Guarded"), Wait(1), Result(1), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(held.Tool()).WithTool(AIFunctionFactory.Create(() => "guarded ran", "Guarded"))
            .AllowBackground("Slow").RequireApproval("Guarded").Build();
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse paused = await agent.RunAsync("go", session);
        ToolApprovalRequestContent request = Assert.Single(paused.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        Assert.False(held.Cancelled.IsCompleted);
        held.Release("slow result");
        AgentResponse answered = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);

        Assert.Equal("done", answered.Text);
        Assert.Equal("Task 1 (Slow) completed. Its result:\nslow result", Answer(model, 4));
    }

    [Fact]
    public async Task ApprovalPause_FollowedByAnyOtherInput_CancelsThePausedRunsTasks()
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Call("Guarded"), Call("ListTasks"), Text("done"));
        ConcurrentQueue<AgentEvent> events = [];
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(held.Tool()).WithTool(AIFunctionFactory.Create(() => "guarded ran", "Guarded"))
            .AllowBackground("Slow").RequireApproval("Guarded").OnEvent(events.Enqueue).Build();
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse paused = await agent.RunAsync("go", session);
        ToolApprovalRequestContent request = Assert.Single(paused.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Empty(events.OfType<BackgroundTaskEnded>());
        // MAF refuses a session's next input while an approval request is unanswered, so the other input comes with the answer.
        await agent.RunAsync([new ChatMessage(ChatRole.User, [request.CreateResponse(false)]), new ChatMessage(ChatRole.User, "something else")], session);

        Assert.True(held.Cancelled.IsCompleted);
        Assert.Equal(BackgroundTaskState.Cancelled, Assert.Single(events.OfType<BackgroundTaskEnded>()).State);
        Assert.Equal("There are no background tasks in this run.", Answer(model, 3));
    }

    private sealed class GuardedTools : ToolCollection
    {
        [Tool("Marked", "Does guarded work.", RequiresApproval = true)]
        public static string Marked() => "marked ran";
    }

    [Theory]
    [InlineData("Named")]
    [InlineData("Marked")]
    public async Task ToolThatNeedsApproval_AndIsAllowedInBackground_IsApprovedFirst_ThenStartsAsATask(string tool)
    {
        ScriptedChatClient model = new(Call(tool, InBackground), Wait(1), Result(1), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(AIFunctionFactory.Create(() => "named ran", "Named")).WithTools(new GuardedTools())
            .RequireApproval("Named").AllowBackground("Named", "Marked").Build();
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse paused = await agent.RunAsync("go", session);
        ToolApprovalRequestContent request = Assert.Single(paused.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);

        AIFunction offered = model.Options[0]!.Tools!.OfType<AIFunction>().Single(item => item.Name == tool);
        Assert.True(offered.JsonSchema.GetProperty("properties").TryGetProperty("runInBackground", out _));
        Assert.Equal($"Started task 1 ({tool}). WaitForTasks returns its result once it has ended.", Answer(model, 1));
        Assert.Equal($"Task 1 ({tool}) completed. Its result:\n{tool.ToLowerInvariant()} ran", Answer(model, 3));
    }

    [Fact]
    public async Task RunSubAgent_RunsInTheBackgroundUnchanged_AndItsEventsCarryTheCallingRun()
    {
        ScriptedChatClient sub = new(Text("sub answer"));
        ScriptedChatClient model = new(
            Call("RunSubAgent", new() { ["modelName"] = "fast", ["subAgentName"] = "Finder", ["role"] = "You find.", ["prompt"] = "Find it.", ["runInBackground"] = true }),
            Wait(1), Result(1), Text("done"));
        ConcurrentQueue<AgentEvent> events = [];
        AIAgent agent = model.CreateAgent().WithRole("r").WithTools(new SubAgentToolCollection([new SubAgentModel("fast", "Use fast for tests.", sub)]))
            .AllowBackground("RunSubAgent").OnEvent(events.Enqueue).Build();

        await agent.RunAsync("go");

        Assert.Equal("Task 1 (RunSubAgent) completed. Its result:\nsub answer", Answer(model, 3));
        Guid parent = events.OfType<RunStarted>().Single(item => item.ParentRunId is null).RunId;
        RunStarted nested = Assert.Single(events.OfType<RunStarted>(), item => item.ParentRunId is not null);
        Assert.Equal((parent, "Finder"), (nested.ParentRunId, nested.AgentName));
    }

    [Fact]
    public async Task WaitForAll_ReturnsOnceEveryListedTaskHasEnded_WithEachResult()
    {
        Held first = new();
        Held second = new();
        ScriptedChatClient model = new(Call("A", InBackground), Call("B", InBackground),
            Call("WaitForTasks", new() { ["taskIds"] = new[] { 1, 2 }, ["waitForAll"] = true }), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(first.Tool("A")).WithTool(second.Tool("B")).AllowBackground("A", "B").Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task run = agent.RunAsync("go", session);
        BackgroundTaskStore? store = null;
        await WaitUntil(() => (store = agent.GetService<BackgroundRunAgent>()!.Find(session))?.Find(2) is { Waiters: > 0 });
        first.Release("one");
        await store!.Find(1)!.Ended;
        // The first task has ended and the wait goes on, because it waits for both.
        Assert.Equal(3, model.Requests.Count);
        second.Release("two");
        await run;

        Assert.Equal("Task 1 (A) completed. Its result:\none\n\nTask 2 (B) completed. Its result:\ntwo", Answer(model, 3));
        Assert.Equal(4, model.Requests.Count);
    }

    [Fact]
    public async Task TaskIds_GoOnCountingInTheNextRunOnTheSameSession()
    {
        ScriptedChatClient model = new(Call("Quick", new() { ["text"] = "a", ["runInBackground"] = true }), Wait(1), Text("first"),
            Call("Quick", new() { ["text"] = "b", ["runInBackground"] = true }), Result(1), Wait(2), Text("second"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(Quick()).AllowBackground("Quick").Build();
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("go", session);
        await agent.RunAsync("again", session);

        Assert.StartsWith("Started task 2 (Quick).", Answer(model, 4));
        // The first run's task is gone, and its id does not come to mean the new task.
        Assert.Equal("Error: No task 1. The tasks of this run are: 2.", Answer(model, 5));
        Assert.Equal("Task 2 (Quick) completed. Its result:\ndid b", Answer(model, 6));
    }

    [Fact]
    public async Task SecondRunOnASessionWhileOneIsInProgress_ThrowsSayingSo()
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Wait(1), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(held.Tool()).AllowBackground("Slow").Build();
        AgentSession session = await agent.CreateSessionAsync();

        Task run = agent.RunAsync("go", session);
        await WaitUntil(() => agent.GetService<BackgroundRunAgent>()!.Find(session)?.Find(1) is { Waiters: > 0 });
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("too", session));
        held.Release("slow result");
        await run;

        Assert.Contains("A run is already in progress on this session", error.Message);
        Assert.Equal("Task 1 (Slow) completed. Its result:\nslow result", Answer(model, 2));
    }

    [Fact]
    public async Task StreamedRunTheCallerStopsReading_CancelsItsRunningTasks()
    {
        Held held = new();
        ScriptedChatClient model = new(Call("Slow", InBackground), Text("started it"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(held.Tool()).AllowBackground("Slow").Build();

        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync("go"))
        {
            if (update.Text.Contains("started it"))
            {
                break;
            }
        }

        await held.Cancelled.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task TaskEndingAfterTheLimitOnWakes_EndsTheRun_CancelsWhatStillRuns_AndSaysSoInAnEventAndTheLog()
    {
        Held first = new();
        Held second = new();
        Held third = new();
        ScriptedChatClient model = new(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("a", "A", new Dictionary<string, object?>(InBackground)),
                new FunctionCallContent("b", "B", new Dictionary<string, object?>(InBackground)),
                new FunctionCallContent("c", "C", new Dictionary<string, object?>(InBackground))
            ]),
            Text("started all three"), Text("one is in"));
        RecordingLoggerFactory loggers = new();
        ConcurrentQueue<AgentEvent> events = [];
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(first.Tool("A")).WithTool(second.Tool("B")).WithTool(third.Tool("C")).AllowBackground("A", "B", "C")
            .ConfigureBackground(options => options.MaxWakes = 1).WithLoggerFactory(loggers).OnEvent(events.Enqueue).Build();

        Task<AgentResponse> run = agent.RunAsync("go");
        await WaitUntil(() => events.OfType<BackgroundWaitStarted>().Count() == 1);
        first.Release("one");
        // The one wake the limit allows is used here; the run waits again for the two tasks left.
        await WaitUntil(() => events.OfType<BackgroundWaitStarted>().Count() == 2);
        second.Release("two");
        AgentResponse response = await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("one is in", response.Text);
        Assert.Equal(3, model.Requests.Count);
        BackgroundWakeLimitReached limit = Assert.Single(events.OfType<BackgroundWakeLimitReached>());
        Assert.Equal(1, limit.Limit);
        Assert.Equal([3], limit.CancelledTaskIds);
        Assert.True(third.Cancelled.IsCompleted);
        Assert.Contains(loggers.Logger.Entries, entry => entry.Message.Contains("reached its limit of 1 wakes with 1 background tasks still running"));
    }

    [Fact]
    public void AllowBackground_NamingAToolTheBuilderLacks_ThrowsNamingTheToolsThereAre()
    {
        AgentBuilder builder = new ScriptedChatClient(Text("ok")).CreateAgent().WithRole("r").WithTool(Quick()).AllowBackground("Missing");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("AllowBackground names 'Missing', but the builder has no such tool. Its tools are:", error.Message);
        Assert.Contains("'Quick'", error.Message);
    }

    [Fact]
    public void AllowBackground_NamingAToolThatAlreadyHasTheParameter_ThrowsNamingTheFix()
    {
        AIFunction clash = AIFunctionFactory.Create((bool runInBackground) => "x", "Clash");
        AgentBuilder builder = new ScriptedChatClient(Text("ok")).CreateAgent().WithRole("r").WithTool(clash).AllowBackground("Clash");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("already has a parameter named 'runInBackground'", error.Message);
        Assert.Contains("Rename that parameter, or remove 'Clash' from AllowBackground.", error.Message);
    }

    [Fact]
    public void ConfigureBackground_WithoutAllowBackground_OrWithALimitThatCannotWork_ThrowsNamingTheFix()
    {
        AgentBuilder alone = new ScriptedChatClient(Text("ok")).CreateAgent().WithRole("r").ConfigureBackground(options => options.MaxRunningTasks = 2);
        AgentBuilder zero = new ScriptedChatClient(Text("ok")).CreateAgent().WithRole("r").WithTool(Quick()).AllowBackground("Quick")
            .ConfigureBackground(options => options.MaxRunningTasks = 0);

        Assert.Contains("Name the tools with AllowBackground, or remove ConfigureBackground.", Assert.Throws<InvalidOperationException>(() => alone.Build()).Message);
        Assert.Contains("BackgroundOptions.MaxRunningTasks must be at least 1", Assert.Throws<InvalidOperationException>(() => zero.Build()).Message);
    }

    [Fact]
    public void BuildOptions_WithABackgroundTool_ThrowsBecauseItBuildsNoAgent()
    {
        AgentBuilder builder = new ScriptedChatClient(Text("ok")).CreateAgent().WithRole("r").WithTool(Quick()).AllowBackground("Quick");

        Assert.Contains("Call Build() instead, or remove AllowBackground, WithInbox and ConfigureBackground.", Assert.Throws<InvalidOperationException>(() => builder.BuildOptions()).Message);
    }

    [Fact]
    public void ConfigureAgentOptions_ThatSwitchesOffWhatTheInboxNeeds_FailsTheBuildNamingBothSettings()
    {
        AgentBuilder builder = new ScriptedChatClient(Text("ok")).CreateAgent().WithRole("r").WithTool(Quick()).AllowBackground("Quick")
            .ConfigureAgentOptions(options => options.RequirePerServiceCallChatHistoryPersistence = false);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("EnableMessageInjection and RequirePerServiceCallChatHistoryPersistence", error.Message);
        Assert.Contains("Leave both on, or remove AllowBackground and WithInbox.", error.Message);
    }

    [Fact]
    public void ConfigureToolLoop_WithAnInbox_BuildsWithAWarningAboutTheLaterMessage()
    {
        RecordingLoggerFactory loggers = new();

        new ScriptedChatClient(Text("ok")).CreateAgent().WithRole("r").WithTool(Quick()).AllowBackground("Quick")
            .ConfigureToolLoop(loop => loop.MaximumIterationsPerRequest = 5).WithLoggerFactory(loggers).Build();

        Assert.Contains(loggers.Logger.Entries, entry => entry.Message.Contains("is read one model call later"));
    }

    [AgentRole("You build.")]
    [AgentAllowBackground("Compile")]
    private sealed class BuildAgent(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        [Tool("Compile", "Compiles the solution.")]
        public static string Compile() => "compiled";
    }

    [Fact]
    public async Task AgentAllowBackgroundAttribute_AllowsTheNamedToolOfAnAgentClass()
    {
        ScriptedChatClient model = new(Call("Compile", InBackground), Wait(1), Text("done"));

        await new BuildAgent(model).RunAsync("go");

        Assert.Equal("Task 1 (Compile) completed. Its result:\ncompiled", Answer(model, 2));
    }

    private static async Task Drain(IAsyncEnumerable<AgentResponseUpdate> updates)
    {
        await foreach (AgentResponseUpdate _ in updates)
        {
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, limit.Token);
        }
    }
}
