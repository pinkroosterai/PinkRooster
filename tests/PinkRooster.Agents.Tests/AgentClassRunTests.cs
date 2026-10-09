using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;

using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.Tests;

namespace PinkRooster.Agents.Tests;

public sealed class AgentClassRunTests
{
    private static ChatMessage Reply(string text) => new(ChatRole.Assistant, text);

    private static ChatMessage Call(string name) => new(ChatRole.Assistant, [new FunctionCallContent($"call-{name}", name, new Dictionary<string, object?>())]);

    private sealed record Verdict(string Decision);

    [AgentRole("You answer.")]
    private sealed class Plain(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You use tools.")]
    private sealed class WithTools(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        [Tool("Own", "Counts things.", RequiresApproval = true)]
        public string Own() => "own";

        [Tool("Free", "Needs no approval.")]
        public string Free() => "free";

        protected override void Configure(AgentBuilder agent) => agent.WithTools(new TestCollections.Guarded()).RequireApproval("Free");
    }

    [AgentRole("You clash.")]
    private sealed class ClashingTool(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        [Tool("Delete", "Same name as the collection's.")]
        public string Delete() => "x";

        protected override void Configure(AgentBuilder agent) => agent.WithTools(new TestCollections.Guarded());
    }

    [AgentRole("You count.")]
    private sealed class Counting(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        private int configures;

        public int Configures => Volatile.Read(ref configures);

        protected override void Configure(AgentBuilder agent)
        {
            Interlocked.Increment(ref configures);
            Thread.Sleep(50);
        }
    }

    [AgentRole("You listen.")]
    private sealed class Listening(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        public List<string> Heard { get; } = [];

        protected override void Configure(AgentBuilder agent) => agent.OnEvent(item => { lock (Heard) Heard.Add($"configure:{item.GetType().Name}"); });

        protected override void OnEvent(AgentEvent item)
        {
            lock (Heard) Heard.Add($"class:{item.GetType().Name}");
        }
    }

    [Fact]
    public async Task AgentClass_RunsAsAnAIAgent_TwoRunsOnOneSession()
    {
        ScriptedChatClient client = new(Reply("one"), Reply("two"));
        AIAgent agent = new Plain(client);
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse first = await agent.RunAsync("a", session);
        AgentResponse second = await agent.RunAsync("b", session);

        Assert.Equal("one", first.Text);
        Assert.Equal("two", second.Text);
        Assert.Equal(["a", "one", "b"], client.Requests[1].Select(message => message.Text));
    }

    [Fact]
    public async Task AgentClass_StreamsARun()
    {
        AIAgent agent = new Plain(new ScriptedChatClient(Reply("streamed")));

        string text = "";
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync("go"))
        {
            text += update.Text;
        }

        Assert.Equal("streamed", text);
    }

    [Fact]
    public async Task AgentClass_AnswersATypedResult_ThroughRunAsyncOfT()
    {
        AIAgent agent = new Plain(new ScriptedChatClient(Reply("""{"decision":"approve"}""")));

        AgentResponse<Verdict> answer = await agent.RunAsync<Verdict>("review");

        Assert.Equal("approve", answer.Result.Decision);
    }

    [Fact]
    public async Task OwnToolAndAConfiguredCollection_AreBothOffered()
    {
        ScriptedChatClient client = new(Reply("ok"));

        await new WithTools(client).RunAsync("go");

        Assert.Equal(["Own", "Free", "Delete"], client.Options[0]!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task AsAgentBuilder_HoldsTheClassDeclarations_AndTakesMore()
    {
        ScriptedChatClient client = new(Reply("ok"));

        AIAgent agent = new WithTools(client).AsAgentBuilder().WithTool(AIFunctionFactory.Create(() => "extra", "Extra", "Added by the caller.")).Build();
        await agent.RunAsync("go");

        Assert.Equal(["Extra", "Own", "Free", "Delete"], client.Options[0]!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public void AsAgentBuilder_ReturnsAnIndependentBuilder_EachCall()
    {
        WithTools source = new(new ScriptedChatClient(Reply("ok")));

        AgentBuilder first = source.AsAgentBuilder().WithName("First");
        AgentBuilder second = source.AsAgentBuilder();

        Assert.NotSame(first, second);
        Assert.Equal("First", first.Build().Name);
        Assert.Equal(nameof(WithTools), second.Build().Name);
    }

    [Fact]
    public async Task AsAgentBuilder_AgentDoesNotReachHandlersAddedToTheInstance()
    {
        ScriptedChatClient client = new(Reply("ok"));
        Plain source = new(client);
        int heard = 0;
        using IDisposable handler = source.OnEvent(_ => heard++);

        await source.AsAgentBuilder().Build().RunAsync("go");

        Assert.Equal(0, heard);
    }

    [Fact]
    public void AsAgentBuilder_ForAClassWithoutARole_ThrowsOnBuild_NamingTheClass()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new NoRole(new ScriptedChatClient(Reply("ok"))).AsAgentBuilder().Build());

        Assert.Contains("NoRole", error.Message);
    }

    private sealed class NoRole(IChatClient chatClient) : DeclaredAgent(chatClient);

    [Theory]
    [InlineData("Own")]
    [InlineData("Free")]
    [InlineData("Delete")]
    public async Task OwnToolsAndCollectionTools_KeepTheirApprovalMarks(string tool)
    {
        AIAgent agent = new WithTools(new ScriptedChatClient(Call(tool), Reply("done")));

        AgentResponse asked = await agent.RunAsync("go");

        Assert.Single(asked.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
    }

    [Fact]
    public async Task OwnTool_RunsWhenTheModelCallsIt()
    {
        ScriptedChatClient client = new(Call("Free"), Reply("done"));
        AIAgent agent = new Plain2(client);

        AgentResponse answer = await agent.RunAsync("go");

        Assert.Equal("done", answer.Text);
        Assert.Contains(client.Requests[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>(), result => result.Result?.ToString() == "free");
    }

    [AgentRole("You use tools.")]
    private sealed class Plain2(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        [Tool("Free", "Needs no approval.")]
        public string Free() => "free";
    }

    [Fact]
    public void OwnToolSharingANameWithACollectionsTool_FailsNamingTheClassAsASource()
    {
        AIAgent agent = new ClashingTool(new ScriptedChatClient(Reply("ok")));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());

        Assert.Contains("'ClashingTool'", error.Message);
        Assert.Contains("Duplicate tool name 'Delete'", error.Message);
        Assert.Contains("collection 'ClashingTool'", error.Message);
    }

    [Fact]
    public async Task Configure_RunsOncePerInstance_ForTwoRuns()
    {
        Counting agent = new(new ScriptedChatClient(Reply("a"), Reply("b")));

        await agent.RunAsync("1");
        await agent.RunAsync("2");

        Assert.Equal(1, agent.Configures);
    }

    [Fact]
    public async Task Configure_RunsOnce_WhenTwoFirstRunsStartTogether()
    {
        Counting agent = new(new ScriptedChatClient(Reply("a"), Reply("b"), Reply("c"), Reply("d")));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => agent.RunAsync("go"))));

        Assert.Equal(1, agent.Configures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnEventOverride_HearsRunStartedFirstAndOneRunCompletedLast_AfterConfiguresHandlers(bool streaming)
    {
        Listening agent = new(new ScriptedChatClient(Reply("hi")));

        if (streaming)
        {
            await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync("go")) { }
        }
        else
        {
            await agent.RunAsync("go");
        }

        Assert.Equal("class:RunStarted", agent.Heard[0]);
        Assert.Equal("configure:RunStarted", agent.Heard[1]);
        Assert.Equal("class:RunCompleted", agent.Heard[^2]);
        Assert.Equal("configure:RunCompleted", agent.Heard[^1]);
        Assert.Single(agent.Heard, entry => entry == "class:RunCompleted");
    }

    [Fact]
    public async Task PublicOnEvent_HearsARunsEvents_AndAHandlerAddedAfterOneRunHearsTheNext()
    {
        Plain agent = new(new ScriptedChatClient(Reply("one"), Reply("two")));
        List<AgentEvent> all = [];
        List<RunCompleted> typed = [];
        using IDisposable first = agent.OnEvent(all.Add);
        await agent.RunAsync("a");
        int afterFirst = all.Count;

        using IDisposable late = agent.OnEvent<RunCompleted>(typed.Add);
        await agent.RunAsync("b");

        Assert.IsType<RunStarted>(all[0]);
        Assert.IsType<RunCompleted>(all[afterFirst - 1]);
        Assert.True(all.Count > afterFirst);
        Assert.Single(typed);
    }

    [Fact]
    public async Task PublicOnEventOfT_HearsOnlyThatEventType()
    {
        Plain agent = new(new ScriptedChatClient(Reply("one")));
        List<RunStarted> started = [];
        using IDisposable subscription = agent.OnEvent<RunStarted>(started.Add);

        await agent.RunAsync("a");

        Assert.Single(started);
    }

    [Fact]
    public async Task DisposedHandler_HearsTheFirstRunAndNothingOfTheSecond()
    {
        Plain agent = new(new ScriptedChatClient(Reply("one"), Reply("two")));
        List<AgentEvent> heard = [];
        IDisposable subscription = agent.OnEvent(heard.Add);
        await agent.RunAsync("a");
        int afterFirst = heard.Count;

        subscription.Dispose();
        subscription.Dispose();
        await agent.RunAsync("b");

        Assert.NotEqual(0, afterFirst);
        Assert.Equal(afterFirst, heard.Count);
    }

    [Fact]
    public async Task HandlerAddedWhileARunIsInProgress_HearsOnlyTheNextRun()
    {
        TaskCompletionSource inModel = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingClient client = new(inModel, release);
        Plain agent = new(client);
        Task<AgentResponse> running = agent.RunAsync("a");
        await inModel.Task;
        List<AgentEvent> heard = [];

        using IDisposable subscription = agent.OnEvent(heard.Add);
        release.SetResult();
        await running;
        Assert.Empty(heard);

        await agent.RunAsync("b");
        Assert.NotEmpty(heard);
    }

    private sealed class BlockingClient(TaskCompletionSource inModel, TaskCompletionSource release) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            inModel.TrySetResult();
            await release.Task;
            return new ChatResponse(Reply("ok"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [AgentRole("You keep context.")]
    private sealed class WithContext(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        [Tool("Ping", "Pings.")]
        public string Ping() => "pong";

        protected override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("## Own context\nfrom the agent");

        protected override void Configure(AgentBuilder agent) => agent.WithTools(new TestCollections.Note("## Collection context\nfrom a collection"));
    }

    [Fact]
    public async Task GetContextAsync_IsInEveryModelCall_AheadOfACollectionsContext_AndNotInTheHistory()
    {
        ScriptedChatClient client = new(Call("Ping"), Reply("done"));
        AIAgent agent = new WithContext(client);
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("go", session);

        Assert.Equal(2, client.Requests.Count);
        Assert.All(client.Requests, request =>
        {
            string context = request[^1].Text;
            Assert.Contains("from the agent", context);
            Assert.True(context.IndexOf("from the agent", StringComparison.Ordinal) < context.IndexOf("from a collection", StringComparison.Ordinal));
        });
        Assert.True(session.TryGetInMemoryChatHistory(out List<ChatMessage>? stored));
        Assert.DoesNotContain(stored!, message => message.Text.Contains("from the agent"));
    }

    [AgentRole("You see your session.")]
    private sealed class SessionAware(IChatClient chatClient) : DeclaredAgent(chatClient)
    {
        public ConcurrentBag<string> Handled { get; } = [];

        [Tool("WhoAmI", "Names the session.")]
        public string WhoAmI() => Who();

        protected override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>($"## Session\nwho={Who()}");

        internal static string Who()
        {
            AgentSession? session = CurrentRunContext?.Session;
            if (session is null)
            {
                return "NO SESSION";
            }
            if (!session.StateBag.TryGetValue("who", out string? who) || who is null)
            {
                who = Guid.NewGuid().ToString("N");
                session.StateBag.SetValue("who", who);
            }
            return who;
        }
    }

    private sealed record ProbedCall(string User, string ToolResult, string Context);

    private sealed class ProbeClient(int parties) : IChatClient
    {
        private readonly TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;

        public ConcurrentBag<ProbedCall> Calls { get; } = [];

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            List<ChatMessage> list = [.. messages];
            string? toolResult = list.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(result => result.Result?.ToString()).FirstOrDefault();
            if (toolResult is null)
            {
                if (Interlocked.Increment(ref arrived) == parties)
                {
                    allArrived.TrySetResult();
                }
                await allArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                return new ChatResponse(Call("WhoAmI"));
            }

            string user = list.First(message => message.Role == ChatRole.User).Text;
            Calls.Add(new ProbedCall(user, toolResult, list[^1].Text));
            return new ChatResponse(Reply("done"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public async Task OneInstanceOnTwoSessions_GivesEachModelCallItsOwnSessionsText_ToTheToolTheContextAndAHandler()
    {
        ProbeClient client = new(parties: 2);
        SessionAware agent = new(client);
        ConcurrentBag<string> handlerSaw = [];
        using IDisposable subscription = agent.OnEvent<RunCompleted>(_ => handlerSaw.Add(SessionAware.Who()));
        AgentSession alice = await agent.CreateSessionAsync();
        AgentSession bob = await agent.CreateSessionAsync();
        alice.StateBag.SetValue("who", "alice");
        bob.StateBag.SetValue("who", "bob");

        await Task.WhenAll(Task.Run(() => agent.RunAsync("for alice", alice)), Task.Run(() => agent.RunAsync("for bob", bob)));

        ProbedCall forAlice = Assert.Single(client.Calls, call => call.User == "for alice");
        ProbedCall forBob = Assert.Single(client.Calls, call => call.User == "for bob");
        Assert.Equal("alice", forAlice.ToolResult);
        Assert.Contains("who=alice", forAlice.Context);
        Assert.Equal("bob", forBob.ToolResult);
        Assert.Contains("who=bob", forBob.Context);
        Assert.Equal(["alice", "bob"], handlerSaw.Order());
    }

    [Fact]
    public async Task OneInstanceRunWithoutASession_StillFindsASession_InTheToolTheContextAndAHandler()
    {
        ProbeClient client = new(parties: 2);
        SessionAware agent = new(client);
        ConcurrentBag<string> handlerSaw = [];
        using IDisposable subscription = agent.OnEvent<RunCompleted>(_ => handlerSaw.Add(SessionAware.Who()));

        await Task.WhenAll(Task.Run(() => agent.RunAsync("one")), Task.Run(() => agent.RunAsync("two")));

        ProbedCall one = Assert.Single(client.Calls, call => call.User == "one");
        ProbedCall two = Assert.Single(client.Calls, call => call.User == "two");
        Assert.NotEqual("NO SESSION", one.ToolResult);
        Assert.NotEqual("NO SESSION", two.ToolResult);
        Assert.NotEqual(one.ToolResult, two.ToolResult);
        Assert.Contains($"who={one.ToolResult}", one.Context);
        Assert.Contains($"who={two.ToolResult}", two.Context);
        Assert.Equal(new[] { one.ToolResult, two.ToolResult }.Order(), handlerSaw.Order());
    }

    [Fact]
    public async Task OneInstanceOnTwoSessions_GivesEachItsOwnAnswer()
    {
        EchoClient client = new();
        AIAgent agent = new Plain(client);
        AgentSession first = await agent.CreateSessionAsync();
        AgentSession second = await agent.CreateSessionAsync();

        AgentResponse[] answers = await Task.WhenAll(Task.Run(() => agent.RunAsync("alpha", first)), Task.Run(() => agent.RunAsync("beta", second)));

        Assert.Equal("echo alpha", answers[0].Text);
        Assert.Equal("echo beta", answers[1].Text);
    }

    private sealed class EchoClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(20, cancellationToken);
            return new ChatResponse(Reply($"echo {messages.Last(message => message.Role == ChatRole.User).Text}"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public void GetService_OfTheInnerAgent_ReturnsAChatClientAgent_AndOfTheClassItself()
    {
        Plain agent = new(new ScriptedChatClient(Reply("x")));

        Assert.NotNull(agent.GetService<ChatClientAgent>());
        Assert.Same(agent, agent.GetService<Plain>());
        Assert.Same(agent, agent.GetService<AIAgent>());
    }

    [Fact]
    public void AgentClass_UsedAsATool_TakesItsNameFromTheClass()
    {
        Plain agent = new(new ScriptedChatClient(Reply("x")));

        AIFunction function = agent.AsAIFunction();

        Assert.Equal("Plain", function.Name);
    }
}
