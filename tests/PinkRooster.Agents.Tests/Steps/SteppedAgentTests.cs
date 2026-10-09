using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Steps;

using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.Steps;

public sealed class SteppedAgentTests
{
    private static ChatMessage Reply(string text) => new(ChatRole.Assistant, text);

    private static ChatMessage Call(string name) => new(ChatRole.Assistant, [new FunctionCallContent($"call-{name}", name, new Dictionary<string, object?>())]);

    private sealed record Run(int Turns);

    [AgentRole("You translate.")]
    private class Translator(IChatClient chatClient, int maxTurns = 5, int stopAfter = 2) : SteppedAgent(chatClient)
    {
        protected override int MaxTurns => maxTurns;

        protected override ValueTask StartAsync(StepContext step, CancellationToken cancellationToken)
        {
            step.SetState(new Run(1));
            step.Enter("draft");
            return ValueTask.CompletedTask;
        }

        protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken)
        {
            Run run = step.GetState<Run>();
            if (stopAfter > 0 && run.Turns >= stopAfter)
            {
                return ValueTask.FromResult(NextStep.Stop());
            }
            step.SetState(new Run(run.Turns + 1));
            return ValueTask.FromResult(NextStep.Send($"revise{run.Turns}", "Rewrite it in plain words.", isFinal: false));
        }
    }

    [AgentRole("You break.")]
    private sealed class Breaking(IChatClient chatClient, bool inStart) : SteppedAgent(chatClient)
    {
        protected override int MaxTurns => 2;

        protected override ValueTask StartAsync(StepContext step, CancellationToken cancellationToken) =>
            inStart ? throw new InvalidOperationException("start broke") : ValueTask.CompletedTask;

        protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("next broke");
    }

    [AgentRole("You forget.")]
    private sealed class ForgetsStart(IChatClient chatClient) : SteppedAgent(chatClient)
    {
        protected override int MaxTurns => 2;

        protected override ValueTask StartAsync(StepContext step, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken)
        {
            step.GetState<Run>();
            return ValueTask.FromResult(NextStep.Stop());
        }
    }

    [AgentRole("You wrap.")]
    private sealed class Wrapped(IChatClient chatClient, List<string> order) : Translator(chatClient)
    {
        protected override void Configure(AgentBuilder agent) => agent.Use((inner, _) => new Layer(inner, order));
    }

    private sealed class Layer(AIAgent inner, List<string> order) : DelegatingAIAgent(inner)
    {
        protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            order.Add("layer start");
            AgentResponse response = await base.RunCoreAsync(messages, session, options, cancellationToken);
            order.Add("layer end");
            return response;
        }
    }

    [AgentRole("You do the least.")]
    private sealed class Minimal(IChatClient chatClient, int maxTurns, List<int> turns) : SteppedAgent(chatClient)
    {
        protected override int MaxTurns => maxTurns;

        [Tool("Ping", "Pings.", RequiresApproval = true)]
        public string Ping() => "pong";

        protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken)
        {
            turns.Add(step.Turn);
            return ValueTask.FromResult(NextStep.Send($"next{step.Turn}", "Go on."));
        }
    }

    [AgentRole("You end.")]
    private sealed class EndsOnFinal(IChatClient chatClient, List<int> turns) : SteppedAgent(chatClient)
    {
        protected override int MaxTurns => 5;

        protected override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken)
        {
            turns.Add(step.Turn);
            return ValueTask.FromResult(NextStep.Send("last", "Say the last word.", isFinal: true));
        }
    }

    [AgentRole("You use tools.")]
    private sealed class Approving(IChatClient chatClient) : Translator(chatClient)
    {
        [Tool("Ping", "Pings.", RequiresApproval = true)]
        public string Ping() => "pong";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoStepsAndStop_MakeTwoModelCalls_AndEnterEachStep(bool streaming)
    {
        ScriptedChatClient client = new(Reply("first"), Reply("second"));
        Translator agent = new(client);
        List<AgentEvent> events = [];
        using IDisposable subscription = agent.OnEvent(events.Add);

        string text;
        if (streaming)
        {
            text = "";
            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync("translate"))
            {
                text += update.Text;
            }
            Assert.Equal("firstsecond", text);
        }
        else
        {
            text = (await agent.RunAsync("translate")).Text;
            Assert.Equal("second", text);
        }

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal("Rewrite it in plain words.", client.Requests[1][^1].Text);
        Assert.Equal(["draft", "revise1"], events.OfType<StepStarted>().Select(step => step.Label));
        Assert.Single(events.OfType<RunCompleted>());
    }

    [Fact]
    public async Task NextAsyncThatNeverStops_EndsAfterMaxTurns_WithTheLastReplyAsTheAnswer()
    {
        ScriptedChatClient client = new(Reply("a"), Reply("b"), Reply("c"), Reply("d"));
        Translator agent = new(client, maxTurns: 3, stopAfter: 0);

        AgentResponse answer = await agent.RunAsync("go");

        Assert.Equal(3, client.Requests.Count);
        Assert.Equal("c", answer.Text);
    }

    [Fact]
    public void MaxTurnsBelowOne_FailsTheBuild_NamingTheClassAndMaxTurns()
    {
        AIAgent agent = new Translator(new ScriptedChatClient(Reply("a")), maxTurns: 0);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());

        Assert.Contains("'Translator'", error.Message);
        Assert.Contains("MaxTurns", error.Message);
    }

    [Theory]
    [InlineData(true, "start broke")]
    [InlineData(false, "next broke")]
    public async Task StartOrNextThatThrows_FailsTheRun_WithOneRunCompletedThatSaysFailed(bool inStart, string message)
    {
        Breaking agent = new(new ScriptedChatClient(Reply("a"), Reply("b")), inStart);
        List<AgentEvent> events = [];
        using IDisposable subscription = agent.OnEvent(events.Add);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("go"));

        Assert.Equal(message, error.Message);
        Assert.Equal(RunOutcome.Failed, Assert.Single(events.OfType<RunCompleted>()).Outcome);
    }

    [Fact]
    public async Task StateReadBeforeStartAsyncSetIt_FailsSayingStartAsyncSetsIt()
    {
        ForgetsStart agent = new(new ScriptedChatClient(Reply("a")));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync("go"));

        Assert.Contains("StartAsync", error.Message);
        Assert.Contains("SetState", error.Message);
    }

    [Fact]
    public async Task LayerAddedInConfigure_WrapsTheWholeLoop()
    {
        List<string> order = [];
        ScriptedChatClient client = new(Reply("a"), Reply("b"));

        await new Wrapped(client, order).RunAsync("go");

        Assert.Equal(["layer start", "layer end"], order);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task ApprovalPausingAStep_ContinuesAtThatStepWhenTheAnswerArrivesOnTheSameSession()
    {
        ScriptedChatClient client = new(Reply("draft"), Call("Ping"), Reply("revised"));
        Approving agent = new(client);
        List<AgentEvent> events = [];
        using IDisposable subscription = agent.OnEvent(events.Add);
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse paused = await agent.RunAsync("go", session);
        ToolApprovalRequestContent request = Assert.Single(paused.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        events.Clear();
        AgentResponse resumed = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);

        Assert.Equal("revised", resumed.Text);
        Assert.Equal(["revise1"], events.OfType<StepStarted>().Select(step => step.Label));
    }

    [Fact]
    public async Task AgentOverridingOnlyMaxTurnsAndNextAsync_EntersStart_Once_AndSurvivesAnApproval()
    {
        List<int> turns = [];
        ScriptedChatClient client = new(Reply("a"), Call("Ping"), Reply("b"), Reply("c"));
        Minimal agent = new(client, maxTurns: 3, turns);
        List<AgentEvent> events = [];
        using IDisposable subscription = agent.OnEvent(events.Add);
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse paused = await agent.RunAsync("go", session);
        ToolApprovalRequestContent request = Assert.Single(paused.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        AgentResponse resumed = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);

        Assert.Equal("c", resumed.Text);
        // The turn that the approval paused is counted when it finishes, so the third turn is the last one MaxTurns allows.
        Assert.Equal([1, 2], turns);
        Assert.Equal(4, client.Requests.Count);
        Assert.Equal(["start"], events.OfType<StepStarted>().Select(step => step.Label).Take(1));
        Assert.Single(events.OfType<StepStarted>(), step => step.Label == "start");
    }

    [Fact]
    public async Task NewMessageOnTheSameSession_StartsAtTurnZeroAgain()
    {
        List<int> turns = [];
        ScriptedChatClient client = new(Reply("a"), Reply("b"), Reply("c"), Reply("d"));
        Minimal agent = new(client, maxTurns: 2, turns);
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("one", session);
        await agent.RunAsync("two", session);

        Assert.Equal([1, 1], turns);
        Assert.Equal(4, client.Requests.Count);
    }

    [Fact]
    public async Task FinalStep_EndsTheRun_WithoutAskingNextAsyncAgain()
    {
        List<int> turns = [];
        ScriptedChatClient client = new(Reply("first"), Reply("last"), Reply("never"));
        EndsOnFinal agent = new(client, turns);

        AgentResponse response = await agent.RunAsync("go");

        Assert.Equal("last", response.Text);
        Assert.Equal(2, client.Requests.Count);
        Assert.Equal([1], turns);
    }

    [Fact]
    public async Task SessionSerializedWhilePausedAtAnApproval_ContinuesAtThatStepAfterItIsRestored()
    {
        ScriptedChatClient client = new(Reply("draft"), Call("Ping"), Reply("revised"));
        Approving agent = new(client);
        List<AgentEvent> events = [];
        using IDisposable subscription = agent.OnEvent(events.Add);
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse paused = await agent.RunAsync("go", session);
        ToolApprovalRequestContent request = Assert.Single(paused.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        System.Text.Json.JsonElement saved = await agent.SerializeSessionAsync(session);
        AgentSession restored = await agent.DeserializeSessionAsync(saved);
        events.Clear();
        AgentResponse resumed = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), restored);

        Assert.Equal("revised", resumed.Text);
        Assert.Equal(["revise1"], events.OfType<StepStarted>().Select(step => step.Label));
    }

    [Fact]
    public async Task OneInstanceOnTwoSessions_KeepsEachSessionsStepStateItsOwn()
    {
        EchoClient client = new();
        Translator agent = new(client, maxTurns: 6, stopAfter: 3);
        AgentSession first = await agent.CreateSessionAsync();
        AgentSession second = await agent.CreateSessionAsync();

        await Task.WhenAll(Task.Run(() => agent.RunAsync("alpha", first)), Task.Run(() => agent.RunAsync("beta", second)));

        Assert.Equal(3, client.CallsBy["alpha"]);
        Assert.Equal(3, client.CallsBy["beta"]);
    }

    private sealed class EchoClient : IChatClient
    {
        public ConcurrentDictionary<string, int> CallsBy { get; } = new();

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(10, cancellationToken);
            string who = messages.First(message => message.Role == ChatRole.User).Text;
            CallsBy.AddOrUpdate(who, 1, (_, count) => count + 1);
            return new ChatResponse(Reply($"reply to {who}"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
