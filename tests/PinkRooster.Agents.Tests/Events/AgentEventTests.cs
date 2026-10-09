using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.ToolCollections.Tests;


namespace PinkRooster.Agents.Tests.Events;

public sealed class AgentEventTests
{
    private static ChatMessage Call(string id, string name, Dictionary<string, object?>? arguments = null) =>
        new(ChatRole.Assistant, [new FunctionCallContent(id, name, arguments ?? [])]);

    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolCallingRun_PublishesEachStepInOrder(bool streaming)
    {
        Recorder recorder = new();
        ScriptedChatClient model = new(Call("c1", "Now", new() { ["zone"] = "utc" }), Text("It is noon."));
        AIAgent agent = model.CreateAgent().WithRole("r").WithName("clock").WithTool((string zone) => "12:00", "Now").OnEvent(recorder.Add).Build();

        await Run(agent, "time?", streaming);

        Assert.Equal(
            [typeof(RunStarted), typeof(ModelCallStarted), typeof(ToolCallRequested), typeof(ModelCallCompleted), typeof(ToolCallStarted), typeof(ToolCallCompleted), typeof(ModelCallStarted), typeof(AssistantTextDelta), typeof(AssistantTextCompleted), typeof(ModelCallCompleted), typeof(RunCompleted)],
            recorder.Types);
        ToolCallRequested requested = recorder.Single<ToolCallRequested>();
        Assert.Equal(("c1", "Now", "utc"), (requested.CallId, requested.Name, requested.Arguments["zone"]?.ToString()));
        ToolCallCompleted completed = recorder.Single<ToolCallCompleted>();
        Assert.Equal(ToolCallStatus.Succeeded, completed.Status);
        Assert.Equal("12:00", completed.Result?.ToString());
        Assert.Equal("It is noon.", recorder.Single<AssistantTextCompleted>().Text);
        Assert.Equal(RunOutcome.Succeeded, recorder.Single<RunCompleted>().Outcome);
        Assert.Single(recorder.Events.Select(item => item.RunId).Distinct());
        Assert.All(recorder.Events, item => Assert.Equal(("clock", (Guid?)null), (item.AgentName, item.ParentRunId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReasoningAndText_DeltasJoinToTheirCompletedBlocks(bool streaming)
    {
        Recorder recorder = new();
        ScriptedChatClient model = new(new ChatMessage(ChatRole.Assistant,
            [new TextReasoningContent("Think "), new TextReasoningContent("hard."), new TextContent("An "), new TextContent("answer.")]));
        AIAgent agent = model.CreateAgent().WithRole("r").OnEvent(recorder.Add).Build();

        await Run(agent, "hi", streaming);

        Assert.Equal(
            [typeof(RunStarted), typeof(ModelCallStarted), typeof(ReasoningDelta), typeof(ReasoningDelta), typeof(ReasoningCompleted), typeof(AssistantTextDelta), typeof(AssistantTextDelta), typeof(AssistantTextCompleted), typeof(ModelCallCompleted), typeof(RunCompleted)],
            recorder.Types);
        Assert.Equal("Think hard.", string.Concat(recorder.OfType<ReasoningDelta>().Select(delta => delta.Text)));
        Assert.Equal("Think hard.", recorder.Single<ReasoningCompleted>().Text);
        Assert.Equal("An answer.", string.Concat(recorder.OfType<AssistantTextDelta>().Select(delta => delta.Text)));
        Assert.Equal("An answer.", recorder.Single<AssistantTextCompleted>().Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovalRequiredCall_EndsTheRunAwaitingApproval_ThenRunsWhenApproved(bool streaming)
    {
        Recorder recorder = new();
        int runs = 0;
        ScriptedChatClient model = new(Call("c1", "Now"), Text("done"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(() => ++runs, "Now").RequireApproval("Now").OnEvent(recorder.Add).Build();
        AgentSession session = await agent.CreateSessionAsync();

        IReadOnlyList<ToolApprovalRequestContent> requests = await Run(agent, "go", streaming, session);

        // MAF's tool loop hands a streamed approval request on as soon as it sees the call, so it can come before the model call's end.
        Type[] expected = streaming
            ? [typeof(RunStarted), typeof(ModelCallStarted), typeof(ToolCallRequested), typeof(ToolApprovalRequested), typeof(ModelCallCompleted), typeof(RunCompleted)]
            : [typeof(RunStarted), typeof(ModelCallStarted), typeof(ToolCallRequested), typeof(ModelCallCompleted), typeof(ToolApprovalRequested), typeof(RunCompleted)];
        Assert.Equal(expected, recorder.Types);
        Assert.Equal(RunOutcome.AwaitingApproval, recorder.Single<RunCompleted>().Outcome);
        Assert.Equal("Now", recorder.Single<ToolApprovalRequested>().Name);

        recorder.Clear();
        await Run(agent, new ChatMessage(ChatRole.User, [.. requests.Select(request => request.CreateResponse(true))]), streaming, session);

        Assert.Equal(1, runs);
        Assert.Equal(ToolCallStatus.Succeeded, recorder.Single<ToolCallCompleted>().Status);
        Assert.DoesNotContain(recorder.Events, item => item is ToolCallRequested);
        Assert.Equal(RunOutcome.Succeeded, recorder.Single<RunCompleted>().Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusedCall_IsCompletedAsRejected_AndNeverRuns(bool streaming)
    {
        Recorder recorder = new();
        int runs = 0;
        ScriptedChatClient model = new(Call("c1", "Now"), Text("ok, skipped"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(() => ++runs, "Now").RequireApproval("Now").OnEvent(recorder.Add).Build();
        AgentSession session = await agent.CreateSessionAsync();
        IReadOnlyList<ToolApprovalRequestContent> requests = await Run(agent, "go", streaming, session);
        recorder.Clear();

        await Run(agent, new ChatMessage(ChatRole.User, [.. requests.Select(request => request.CreateResponse(false, "not now"))]), streaming, session);

        Assert.Equal(0, runs);
        ToolCallCompleted rejected = recorder.Single<ToolCallCompleted>();
        Assert.Equal(("c1", "Now", ToolCallStatus.Rejected, "not now"), (rejected.CallId, rejected.Name, rejected.Status, rejected.Result));
        Assert.DoesNotContain(recorder.Events, item => item is ToolCallStarted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailingTool_IsCompletedAsFailed_WithItsException(bool streaming)
    {
        Recorder recorder = new();
        ScriptedChatClient model = new(Call("c1", "Break"), Text("it broke"));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(string () => throw new InvalidOperationException("boom"), "Break").OnEvent(recorder.Add).Build();

        await Run(agent, "go", streaming);

        ToolCallCompleted failed = recorder.Single<ToolCallCompleted>();
        Assert.Equal(ToolCallStatus.Failed, failed.Status);
        Assert.Equal("boom", Assert.IsType<InvalidOperationException>(failed.Error).Message);
        Assert.Equal(RunOutcome.Succeeded, recorder.Single<RunCompleted>().Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingWhileAToolRuns_CompletesTheCallThenTheRunAsCancelled(bool streaming)
    {
        using CancellationTokenSource cancellation = new();
        // A backstop: if ToolCallStarted never arrives, the test fails on the missing events instead of waiting forever.
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        Recorder recorder = new(item =>
        {
            if (item is ToolCallStarted)
            {
                cancellation.Cancel();
            }
        });
        ScriptedChatClient model = new(Call("c1", "Wait"), Text("never"));
        AIAgent agent = model.CreateAgent().WithRole("r")
            .WithTool(async (CancellationToken token) => { await Task.Delay(Timeout.Infinite, token); return "late"; }, "Wait")
            .OnEvent(recorder.Add)
            .Build();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(agent, "go", streaming, cancellationToken: cancellation.Token));

        Assert.Equal(ToolCallStatus.Cancelled, recorder.Single<ToolCallCompleted>().Status);
        Assert.Equal(RunOutcome.Cancelled, recorder.Single<RunCompleted>().Outcome);
        Assert.IsType<RunCompleted>(recorder.Events[^1]);
    }

    [Fact]
    public async Task FailingModel_EndsTheRunAsFailed()
    {
        Recorder recorder = new();
        AIAgent agent = new ScriptedChatClient(Array.Empty<ChatMessage>()).CreateAgent().WithRole("r").OnEvent(recorder.Add).Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("hi"));

        RunCompleted completed = recorder.Single<RunCompleted>();
        Assert.Equal(RunOutcome.Failed, completed.Outcome);
        Assert.IsType<InvalidOperationException>(completed.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentCalledAsATool_ReportsToItsCallersHandlers_MarkedWithTheCallersRun(bool streaming)
    {
        Recorder recorder = new();
        AIAgent inner = new ScriptedChatClient(Text("inner answer")).CreateAgent().WithRole("inner").WithName("Helper").WithDescription("Helps.").Build();
        ScriptedChatClient outerModel = new(Call("c1", "Helper", new() { ["query"] = "q" }), Text("done"));
        AIAgent outer = outerModel.CreateAgent().WithRole("outer").WithName("Main").WithTool(inner.AsAIFunction()).OnEvent(recorder.Add).Build();

        await Run(outer, "go", streaming);

        Guid outerRun = recorder.Events.First(item => item.AgentName == "Main").RunId;
        AgentEvent[] nested = [.. recorder.Events.Where(item => item.AgentName == "Helper")];
        Assert.Equal([typeof(RunStarted), typeof(ModelCallStarted), typeof(AssistantTextDelta), typeof(AssistantTextCompleted), typeof(ModelCallCompleted), typeof(RunCompleted)], nested.Select(item => item.GetType()));
        Assert.All(nested, item => Assert.Equal(outerRun, item.ParentRunId));
        Assert.All(recorder.Events.Where(item => item.AgentName == "Main"), item => Assert.Null(item.ParentRunId));
        // The caller's own events keep their order around the nested run.
        Assert.IsType<RunCompleted>(recorder.Events[^1]);
        Assert.Equal("Main", recorder.Events[^1].AgentName);
    }

    [Fact]
    public async Task HandlerSharedByNestedAgents_HearsEachEventOnce()
    {
        Recorder recorder = new();
        AIAgent inner = new ScriptedChatClient(Text("inner answer")).CreateAgent().WithRole("inner").WithName("Helper").WithDescription("Helps.").OnEvent(recorder.Add).Build();
        AIAgent outer = new ScriptedChatClient(Call("c1", "Helper", new() { ["query"] = "q" }), Text("done"))
            .CreateAgent().WithRole("outer").WithName("Main").WithTool(inner.AsAIFunction()).OnEvent(recorder.Add).Build();

        await outer.RunAsync("go");

        Assert.Single(recorder.Events, item => item is RunStarted && item.AgentName == "Helper");
        Assert.Single(recorder.Events, item => item is AssistantTextCompleted && item.AgentName == "Helper");
    }

    [Fact]
    public async Task ThrowingHandler_IsLogged_WhenTheBuilderHasALogger()
    {
        RecordingLoggerFactory loggers = new();
        AIAgent agent = new ScriptedChatClient(Text("hi")).CreateAgent().WithRole("r")
            .OnEvent(_ => throw new InvalidOperationException("handler broke"))
            .WithLoggerFactory(loggers)
            .Build();

        AgentResponse response = await agent.RunAsync("hi");

        Assert.Equal("hi", response.Text);
        Assert.Contains(loggers.Logger.Errors, entry => entry.Message.Contains("RunStarted"));
    }


    private static Action<AgentEvent> ThrowOn<TEvent>(Recorder recorder, Func<TEvent, bool>? when = null) where TEvent : AgentEvent => item =>
    {
        recorder.Add(item);
        if (item is TEvent typed && (when?.Invoke(typed) ?? true))
        {
            throw new InvalidOperationException("handler broke");
        }
    };


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerThrowingOnRunCompleted_WithALogger_IsLogged_AndTheRunSucceeds(bool streaming)
    {
        Recorder recorder = new();
        RecordingLoggerFactory loggers = new();
        AIAgent agent = new ScriptedChatClient(Text("hi")).CreateAgent().WithRole("r").OnEvent(ThrowOn<RunCompleted>(recorder)).WithLoggerFactory(loggers).Build();

        await Run(agent, "hi", streaming);

        Assert.Equal(RunOutcome.Succeeded, recorder.Single<RunCompleted>().Outcome);
        Assert.Contains(loggers.Logger.Errors, entry => entry.Message.Contains("RunCompleted"));
    }



    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerThrowingOnToolCallCompleted_WithALogger_IsLogged_AndTheRunGoesOn(bool streaming)
    {
        Recorder recorder = new();
        RecordingLoggerFactory loggers = new();
        ScriptedChatClient model = new(Call("c1", "Now"), Text("It is noon."));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(() => "12:00", "Now").OnEvent(ThrowOn<ToolCallCompleted>(recorder)).WithLoggerFactory(loggers).Build();

        await Run(agent, "time?", streaming);

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(RunOutcome.Succeeded, recorder.Single<RunCompleted>().Outcome);
        Assert.Contains(loggers.Logger.Errors, entry => entry.Message.Contains("ToolCallCompleted"));
    }


    [Fact]
    public async Task ThrowingHandler_DoesNotFailTheRun_WithoutALogger()
    {
        AIAgent agent = new ScriptedChatClient(Text("hi")).CreateAgent().WithRole("r")
            .OnEvent(_ => throw new InvalidOperationException("handler broke"))
            .Build();

        AgentResponse response = await agent.RunAsync("hi");

        Assert.Equal("hi", response.Text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HandlerThrowingOnToolCallEvents_NeverChangesTheToolOrTheRun_WithOrWithoutALogger(bool streaming, bool withLogger)
    {
        Recorder recorder = new();
        int runs = 0;
        ScriptedChatClient model = new(Call("c1", "Now"), Text("done"));
        AgentBuilder builder = model.CreateAgent().WithRole("r").WithTool(() => ++runs, "Now")
            .OnEvent(item =>
            {
                recorder.Add(item);
                if (item is ToolCallStarted or ToolCallCompleted)
                {
                    throw new InvalidOperationException("handler broke");
                }
            });
        AIAgent agent = (withLogger ? builder.WithLoggerFactory(new RecordingLoggerFactory()) : builder).Build();

        await Run(agent, "go", streaming);

        Assert.Equal(1, runs);
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(ToolCallStatus.Succeeded, recorder.Single<ToolCallCompleted>().Status);
        Assert.Equal(RunOutcome.Succeeded, recorder.Single<RunCompleted>().Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerThrowingOperationCanceled_IsCaughtLikeAnyOther_AndTheRunSucceeds(bool streaming)
    {
        Recorder recorder = new();
        AIAgent agent = new ScriptedChatClient(Text("hi")).CreateAgent().WithRole("r")
            .OnEvent(item =>
            {
                recorder.Add(item);
                if (item is RunCompleted)
                {
                    throw new OperationCanceledException("handler timed out");
                }
            }).Build();

        await Run(agent, "hi", streaming);

        Assert.Equal(RunOutcome.Succeeded, recorder.Single<RunCompleted>().Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerThrowingOnTheRunsFailure_LeavesTheRunsOwnException(bool streaming)
    {
        Recorder recorder = new();
        AIAgent agent = new ScriptedChatClient(Array.Empty<ChatMessage>()).CreateAgent().WithRole("r").OnEvent(ThrowOn<RunCompleted>(recorder)).Build();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(agent, "hi", streaming));

        Assert.NotEqual("handler broke", error.Message);
        Assert.Single(recorder.OfType<RunCompleted>());
    }

    [Fact]
    public async Task StreamTheConsumerStopsReading_CompletesTheRunAsAbandoned()
    {
        Recorder recorder = new();
        AIAgent agent = new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, [new TextContent("one "), new TextContent("two "), new TextContent("three")]))
            .CreateAgent().WithRole("r").OnEvent(recorder.Add).Build();

        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync("hi"))
        {
            break;
        }

        RunCompleted completed = recorder.Single<RunCompleted>();
        Assert.Equal(RunOutcome.Abandoned, completed.Outcome);
        Assert.Null(completed.Error);
        Assert.IsType<RunCompleted>(recorder.Events[^1]);
        Assert.DoesNotContain(recorder.Types, type => type == typeof(AssistantTextCompleted));
    }

    [Fact]
    public async Task StreamReadToTheEnd_PublishesOneCompletion_WhenDisposedAfterwards()
    {
        Recorder recorder = new();
        AIAgent agent = new ScriptedChatClient(Text("hi")).CreateAgent().WithRole("r").OnEvent(recorder.Add).Build();

        await Run(agent, "hi", streaming: true);

        Assert.Equal(RunOutcome.Succeeded, recorder.Single<RunCompleted>().Outcome);
    }

    [Fact]
    public async Task StreamedRunOfAFailingModel_CompletesOnceAsFailed()
    {
        Recorder recorder = new();
        AIAgent agent = new ScriptedChatClient(Array.Empty<ChatMessage>()).CreateAgent().WithRole("r").OnEvent(recorder.Add).Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(agent, "hi", streaming: true));

        Assert.Equal(RunOutcome.Failed, recorder.Single<RunCompleted>().Outcome);
        Assert.IsType<RunCompleted>(recorder.Events[^1]);
    }

    private static ChatResponse WithUsage(ChatMessage message, long input, long output) =>
        new(message) { ModelId = "scripted-model", FinishReason = ChatFinishReason.Stop, Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output } };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelCalls_ReportUsage_AndTheRunSumsItsOwn(bool streaming)
    {
        Recorder recorder = new();
        ScriptedChatClient model = new(WithUsage(Call("c1", "Now"), 10, 5), WithUsage(Text("It is noon."), 20, 7));
        AIAgent agent = model.CreateAgent().WithRole("r").WithTool(() => "12:00", "Now").OnEvent(recorder.Add).Build();

        await Run(agent, "time?", streaming);

        ModelCallCompleted[] calls = [.. recorder.OfType<ModelCallCompleted>()];
        Assert.Equal(2, calls.Length);
        Assert.Equal(2, recorder.OfType<ModelCallStarted>().Count());
        Assert.All(calls, call => Assert.Equal("scripted-model", call.ModelId));
        Assert.All(calls, call => Assert.Null(call.Error));
        Assert.Equal(15, calls[0].Usage?.TotalTokenCount);
        Assert.Equal(27, calls[1].Usage?.TotalTokenCount);
        UsageDetails? total = recorder.Single<RunCompleted>().Usage;
        Assert.Equal((30L, 12L, 42L), (total?.InputTokenCount, total?.OutputTokenCount, total?.TotalTokenCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelCalls_WithoutUsage_GiveNullUsage(bool streaming)
    {
        Recorder recorder = new();
        AIAgent agent = new ScriptedChatClient(Text("hi")).CreateAgent().WithRole("r").OnEvent(recorder.Add).Build();

        await Run(agent, "hi", streaming);

        Assert.Null(recorder.Single<ModelCallCompleted>().Usage);
        Assert.Null(recorder.Single<RunCompleted>().Usage);
        Assert.True(recorder.Single<RunCompleted>().Duration >= TimeSpan.Zero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailingModelCall_CompletesTheCallOnce_WithItsError(bool streaming)
    {
        Recorder recorder = new();
        AIAgent agent = new ScriptedChatClient(Array.Empty<ChatMessage>()).CreateAgent().WithRole("r").OnEvent(recorder.Add).Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(agent, "hi", streaming));

        Assert.Equal([typeof(RunStarted), typeof(ModelCallStarted), typeof(ModelCallCompleted), typeof(RunCompleted)], recorder.Types);
        Assert.IsType<InvalidOperationException>(recorder.Single<ModelCallCompleted>().Error);
    }

    [Fact]
    public async Task AbandonedStream_CompletesTheOpenModelCall_BeforeTheRun()
    {
        Recorder recorder = new();
        AIAgent agent = new ScriptedChatClient(WithUsage(new ChatMessage(ChatRole.Assistant, [new TextContent("one "), new TextContent("two")]), 3, 4))
            .CreateAgent().WithRole("r").OnEvent(recorder.Add).Build();

        await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync("hi"))
        {
            break;
        }

        Assert.Single(recorder.OfType<ModelCallStarted>());
        Assert.Single(recorder.OfType<ModelCallCompleted>());
        Assert.IsType<RunCompleted>(recorder.Events[^1]);
        Assert.IsType<ModelCallCompleted>(recorder.Events[^2]);
        Assert.Equal(RunOutcome.Abandoned, recorder.Single<RunCompleted>().Outcome);
    }

    [Fact]
    public async Task AgentCalledAsATool_ReportsItsOwnUsage_NotInItsCallersTotal()
    {
        Recorder recorder = new();
        AIAgent inner = new ScriptedChatClient(WithUsage(Text("inner"), 100, 100)).CreateAgent().WithRole("inner").WithName("Helper").WithDescription("Helps.").Build();
        AIAgent outer = new ScriptedChatClient(WithUsage(Call("c1", "Helper", new() { ["query"] = "q" }), 1, 1), WithUsage(Text("done"), 2, 2))
            .CreateAgent().WithRole("outer").WithName("Main").WithTool(inner.AsAIFunction()).OnEvent(recorder.Add).Build();

        await Run(outer, "go", streaming: false);

        RunCompleted[] runs = [.. recorder.OfType<RunCompleted>()];
        Assert.Equal(6, runs.Single(run => run.AgentName == "Main").Usage?.TotalTokenCount);
        Assert.Equal(200, runs.Single(run => run.AgentName == "Helper").Usage?.TotalTokenCount);
    }

    [Fact]
    public async Task TypedHandler_HearsItsTypeAndDerivedTypes_InTheOrderRegistered()
    {
        List<string> heard = [];
        AIAgent agent = new ScriptedChatClient(Text("hi")).CreateAgent().WithRole("r")
            .OnEvent<RunStarted>(_ => heard.Add("typed started"))
            .OnEvent((AgentEvent item) => heard.Add("all " + item.GetType().Name))
            .OnEvent<AgentEvent>(item => heard.Add("base " + item.GetType().Name))
            .OnEvent<RunCompleted>(done => heard.Add("typed completed " + done.Outcome))
            .Build();

        await agent.RunAsync("hi");

        Assert.Equal(["typed started", "all RunStarted", "base RunStarted"], heard.Take(3));
        Assert.Equal("typed completed Succeeded", heard[^1]);
        Assert.DoesNotContain("typed started", heard.Skip(3));
    }

    [Fact]
    public async Task TypedHandler_SurvivesClone()
    {
        int started = 0;
        AgentBuilder builder = new ScriptedChatClient(Text("a")).CreateAgent().WithRole("r").OnEvent<RunStarted>(_ => started++);

        await builder.Clone().Build().RunAsync("hi");

        Assert.Equal(1, started);
    }

    [Fact]
    public void TypedHandler_WithNull_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ScriptedChatClient(Array.Empty<ChatMessage>()).CreateAgent().OnEvent<RunStarted>(null!));
    }

    [Fact]
    public void BuildOptions_RefusesOnEvent_AndNamesBuild()
    {
        AgentBuilder builder = new ScriptedChatClient(Array.Empty<ChatMessage>()).CreateAgent().WithRole("r").OnEvent(_ => { });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(builder.BuildOptions);
        Assert.Contains("Build()", error.Message);
    }

    [Fact]
    public async Task Clone_KeepsTheHandlers()
    {
        Recorder recorder = new();
        AgentBuilder builder = new ScriptedChatClient(Text("a"), Text("b")).CreateAgent().WithRole("r").OnEvent(recorder.Add);

        await builder.Clone().Build().RunAsync("hi");

        Assert.Single(recorder.Events.OfType<RunStarted>());
    }

    private static Task<IReadOnlyList<ToolApprovalRequestContent>> Run(AIAgent agent, string prompt, bool streaming, AgentSession? session = null, CancellationToken cancellationToken = default) =>
        Run(agent, new ChatMessage(ChatRole.User, prompt), streaming, session, cancellationToken);

    private static async Task<IReadOnlyList<ToolApprovalRequestContent>> Run(AIAgent agent, ChatMessage message, bool streaming, AgentSession? session, CancellationToken cancellationToken = default)
    {
        session ??= await agent.CreateSessionAsync(cancellationToken);
        if (!streaming)
        {
            AgentResponse response = await agent.RunAsync(message, session, cancellationToken: cancellationToken);
            return [.. response.Messages.SelectMany(item => item.Contents).OfType<ToolApprovalRequestContent>()];
        }

        List<ToolApprovalRequestContent> requests = [];
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(message, session, cancellationToken: cancellationToken))
        {
            requests.AddRange(update.Contents.OfType<ToolApprovalRequestContent>());
        }
        return requests;
    }

    private sealed class Recorder(Action<AgentEvent>? onEvent = null)
    {
        private readonly List<AgentEvent> events = [];

        public List<AgentEvent> Events
        {
            get
            {
                lock (events)
                {
                    return [.. events];
                }
            }
        }

        public Type[] Types => [.. Events.Select(item => item.GetType())];

        public void Add(AgentEvent item)
        {
            lock (events)
            {
                events.Add(item);
            }
            onEvent?.Invoke(item);
        }

        public void Clear()
        {
            lock (events)
            {
                events.Clear();
            }
        }

        public IEnumerable<T> OfType<T>() => Events.OfType<T>();

        public T Single<T>() => Assert.Single(Events.OfType<T>());
    }
}
