using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Agents.Eventing;
using PinkRooster.ToolCollections.Tests;


namespace PinkRooster.Agents.Tests.Events;

public sealed class AgentLayerTests
{
    private sealed record ConsumerEvent(string Note) : AgentEvent;

    private sealed class Layer(AIAgent inner, string name, List<string> order, bool publish = false) : DelegatingAIAgent(inner)
    {
        protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            Enter();
            return base.RunCoreAsync(messages, session, options, cancellationToken);
        }

        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            Enter();
            return base.RunCoreStreamingAsync(messages, session, options, cancellationToken);
        }

        private void Enter()
        {
            order.Add(name);
            if (publish)
            {
                AgentEvents.Publish(new ConsumerEvent(name));
            }
        }
    }

    private static AgentBuilder Builder() => new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "hi"), new ChatMessage(ChatRole.Assistant, "again")).CreateAgent().WithRole("r");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EventPublishedFromALayer_ReachesHandlers_WithTheRunsId(bool streaming)
    {
        List<AgentEvent> events = [];
        AIAgent agent = Builder().Use((inner, _) => new Layer(inner, "mine", [], publish: true)).OnEvent(events.Add).Build();

        if (streaming)
        {
            await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync("go")) { }
        }
        else
        {
            await agent.RunAsync("go");
        }

        ConsumerEvent mine = Assert.Single(events.OfType<ConsumerEvent>());
        Assert.Equal("mine", mine.Note);
        Assert.Equal(Assert.Single(events.OfType<RunStarted>()).RunId, mine.RunId);
        Assert.IsType<RunCompleted>(events[^1]);
    }

    [Fact]
    public void Publish_OutsideARun_ReturnsFalse()
    {
        Assert.False(AgentEvents.Publish(new ConsumerEvent("nobody listens")));
    }

    [Fact]
    public void Publish_WithNull_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => AgentEvents.Publish(null!));
    }

    [Theory]
    [InlineData(typeof(RunStarted))]
    [InlineData(typeof(RunCompleted))]
    public void Publish_OfALifecycleEvent_IsRefused_AndSaysToDeriveOwnEvent(Type type)
    {
        AgentEvent item = type == typeof(RunStarted) ? new RunStarted() : new RunCompleted(RunOutcome.Succeeded);

        ArgumentException error = Assert.Throws<ArgumentException>(() => AgentEvents.Publish(item));

        Assert.Contains("Derive your own event from AgentEvent", error.Message);
    }

    [Fact]
    public async Task Layers_WrapInTheOrderAdded_EachAroundTheOneBefore()
    {
        List<string> order = [];
        AIAgent agent = Builder()
            .Use((inner, _) => new Layer(inner, "first", order))
            .Use((inner, _) => new Layer(inner, "second", order))
            .Build();

        await agent.RunAsync("go");

        Assert.Equal(["second", "first"], order);
    }

    [Fact]
    public async Task Use_ReceivesTheProviderGivenToWithServices()
    {
        NoServices provider = new();
        IServiceProvider? received = null;

        await Builder().WithServices(provider).Use((inner, services) =>
        {
            received = services;
            return inner;
        }).Build().RunAsync("go");

        Assert.Same(provider, received);
    }

    [Fact]
    public async Task Use_WithoutWithServices_ReceivesAnEmptyProvider()
    {
        IServiceProvider? received = null;

        await Builder().Use((inner, services) =>
        {
            received = services;
            return inner;
        }).Build().RunAsync("go");

        Assert.NotNull(received);
        Assert.Null(received.GetService(typeof(string)));
    }

    [Fact]
    public async Task Clone_KeepsTheLayers()
    {
        List<string> order = [];
        AgentBuilder builder = Builder().Use((inner, _) => new Layer(inner, "kept", order));

        await builder.Clone().Build().RunAsync("go");

        Assert.Equal(["kept"], order);
    }

    [Fact]
    public void BuildOptions_RefusesALayer_AndNamesBuild()
    {
        AgentBuilder builder = Builder().Use((inner, _) => inner);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(builder.BuildOptions);

        Assert.Contains("Build()", error.Message);
        Assert.Contains("Use", error.Message);
    }

    [Fact]
    public void Build_WithALayerReturningNull_NamesUse()
    {
        AgentBuilder builder = Builder().Use((_, _) => null!);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Use", error.Message);
    }

    [Fact]
    public void Use_WithNull_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => Builder().Use(null!));
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
