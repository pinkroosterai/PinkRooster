using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.Tools;

public sealed class ToolCollectionSourceTests
{
    private sealed class Tracked : ExternalToolCollection<Tracked>, IAsyncDisposable
    {
        private readonly string label;

        public Tracked(string name) : base(name, [AIFunctionFactory.Create(() => name, name, "A tool.")])
        {
            label = name;
        }

        public static List<string> Disposed { get; } = [];

        public ValueTask DisposeAsync()
        {
            lock (Disposed)
            {
                Disposed.Add(label);
            }
            return ValueTask.CompletedTask;
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    private sealed class TrackedSourceAttribute(string name) : ToolCollectionSourceAttribute
    {
        public override ValueTask<ToolCollection> CreateAsync(CancellationToken cancellationToken) => ValueTask.FromResult<ToolCollection>(new Tracked(name));
    }

    [AttributeUsage(AttributeTargets.Class)]
    private sealed class FailingSourceAttribute : ToolCollectionSourceAttribute
    {
        public override ValueTask<ToolCollection> CreateAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("server down");
    }

    [TrackedSource("BaseSrc")]
    private abstract class BaseAgent(IChatClient chatClient) : DeclaredAgent(chatClient);

    [AgentRole("You use a sourced collection.")]
    [TrackedSource("DerivedSrc")]
    private sealed class Derived(IChatClient chatClient) : BaseAgent(chatClient);

    [AgentRole("You fail to connect.")]
    [TrackedSource("First")]
    [FailingSource]
    private sealed class Failing(IChatClient chatClient) : DeclaredAgent(chatClient);

    // Connects only when the test says so, and records the token it was given.
    [AttributeUsage(AttributeTargets.Class)]
    private sealed class GatedSourceAttribute : ToolCollectionSourceAttribute
    {
        public static TaskCompletionSource Gate { get; set; } = new();

        public static int Calls { get; set; }

        public override async ValueTask<ToolCollection> CreateAsync(CancellationToken cancellationToken)
        {
            Calls++;
            await Gate.Task.WaitAsync(cancellationToken);
            return new Tracked("Gated");
        }
    }

    [AgentRole("You wait for a server.")]
    [GatedSource]
    private sealed class Gated(IChatClient chatClient) : DeclaredAgent(chatClient);

    private static IChatClient Client() => new TestSupport.ScriptedChatClient(Array.Empty<ChatMessage>());

    [Fact]
    public async Task Run_OffersSourcedCollections_BaseTypesFirst()
    {
        TestSupport.ScriptedChatClient client = new(new ChatMessage[] { new(ChatRole.Assistant, "ok") });
        await using Derived agent = new(client);

        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["BaseSrc", "DerivedSrc"], client.Options[0]!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task Run_DoesNotHoldTheCallingThread_WhileASourcedCollectionConnects()
    {
        GatedSourceAttribute.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TestSupport.ScriptedChatClient client = new(new ChatMessage[] { new(ChatRole.Assistant, "ok") });
        await using Gated agent = new(client);

        Task<AgentResponse> run = agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(run.IsCompleted);
        GatedSourceAttribute.Gate.SetResult();
        Assert.Equal("ok", (await run).Text);
        Assert.Equal(["Gated"], client.Options[0]!.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public async Task Run_PassesItsCancellationTokenToTheConnecting_AndALaterRunConnectsAgain()
    {
        GatedSourceAttribute.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedSourceAttribute.Calls = 0;
        TestSupport.ScriptedChatClient client = new(new ChatMessage[] { new(ChatRole.Assistant, "ok") });
        await using Gated agent = new(client);
        using CancellationTokenSource cancelled = new();

        Task<AgentResponse> run = agent.RunAsync("hi", cancellationToken: cancelled.Token);
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Empty(client.Requests);

        GatedSourceAttribute.Gate.SetResult();
        Assert.Equal("ok", (await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken)).Text);
        Assert.Equal(2, GatedSourceAttribute.Calls);
    }

    [Fact]
    public async Task InitializeAsync_BuildsTheAgentOnce_SoTheFirstRunConnectsNothing()
    {
        GatedSourceAttribute.Gate = new TaskCompletionSource();
        GatedSourceAttribute.Gate.SetResult();
        GatedSourceAttribute.Calls = 0;
        TestSupport.ScriptedChatClient client = new(new ChatMessage[] { new(ChatRole.Assistant, "ok") });
        await using Gated agent = new(client);

        await agent.InitializeAsync(TestContext.Current.CancellationToken);
        await agent.InitializeAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, GatedSourceAttribute.Calls);
    }

    [Fact]
    public async Task InitializeAsync_WhenASourceFails_NamesTheClass_AndDisposesTheEarlierOnes()
    {
        Tracked.Disposed.Clear();
        await using Failing agent = new(Client());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.InitializeAsync(TestContext.Current.CancellationToken));

        Assert.Contains("'Failing'", error.Message);
        Assert.Contains("server down", error.Message);
        Assert.Equal(["First"], Tracked.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheCollectionsTheAttributesCreated()
    {
        Tracked.Disposed.Clear();
        Derived agent = new(Client());
        _ = agent.GetService<ChatClientAgent>();

        await agent.DisposeAsync();

        Assert.Equal(["BaseSrc", "DerivedSrc"], Tracked.Disposed.Order());
    }

    [Fact]
    public async Task Build_WhenALaterSourceFails_DisposesTheEarlierOnes_AndNamesTheClass()
    {
        Tracked.Disposed.Clear();
        await using Failing agent = new(Client());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => agent.GetService<ChatClientAgent>());

        Assert.Contains("'Failing'", error.Message);
        Assert.Contains("server down", error.Message);
        Assert.Equal(["First"], Tracked.Disposed);
    }
}
