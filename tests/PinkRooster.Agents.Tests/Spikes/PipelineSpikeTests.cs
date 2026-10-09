using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;


namespace PinkRooster.Agents.Tests.Spikes;

/// <summary>
/// Pins down how the pinned MAF version's ChatClientAgent treats the client it is given, which the design rests on
///. Kept as regression tests for package upgrades.
/// </summary>
public sealed class PipelineSpikeTests
{
    private const string Marker = "PinkRooster.Spike.Context";

    [Fact]
    public async Task DefaultAgent_WrapsTheClientInExactlyOneToolLoop()
    {
        ScriptedChatClient model = new(new ChatMessage(ChatRole.Assistant, "done"));
        ChatClientAgent agent = new(new MarkingClient(model), new ChatClientAgentOptions());

        await agent.RunAsync("hi");

        List<IChatClient> chain = Chain(agent.ChatClient);
        Log(chain);
        Assert.Single(chain.OfType<FunctionInvokingChatClient>());
        Assert.Contains(chain, client => client is MarkingClient);
        Assert.True(IndexOf<FunctionInvokingChatClient>(chain) < IndexOf<MarkingClient>(chain), "The agent's tool loop must sit above a decorator inside the given client.");
    }

    [Fact]
    public async Task SuppliedToolLoop_IsTheOnlyLoop()
    {
        ScriptedChatClient model = new(new ChatMessage(ChatRole.Assistant, "done"));
        IChatClient client = model.AsBuilder()
            .UseFunctionInvocation(configure: loop => loop.MaximumIterationsPerRequest = 7)
            .Use(inner => new MarkingClient(inner))
            .Build();
        ChatClientAgent agent = new(client, new ChatClientAgentOptions());

        await agent.RunAsync("hi");

        List<IChatClient> chain = Chain(agent.ChatClient);
        Log(chain);
        FunctionInvokingChatClient loop = Assert.Single(chain.OfType<FunctionInvokingChatClient>());
        Assert.Equal(7, loop.MaximumIterationsPerRequest);
    }

    [Fact]
    public async Task SuppliedToolLoop_RunsToolsAndApprovals()
    {
        int pings = 0;
        AIFunction ping = AIFunctionFactory.Create(() => ++pings, "Ping");
        ScriptedChatClient model = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "Ping")]),
            new ChatMessage(ChatRole.Assistant, "pinged"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c2", "Ping")]),
            new ChatMessage(ChatRole.Assistant, "pinged again"));
        IChatClient client = model.AsBuilder().UseFunctionInvocation().Build();

        ChatClientAgent plain = new(client, new ChatClientAgentOptions { ChatOptions = new ChatOptions { Tools = [ping] } });
        AgentResponse first = await plain.RunAsync("ping");
        Assert.Equal(1, pings);
        Assert.Equal("pinged", first.Text);

        ChatClientAgent guarded = new(client, new ChatClientAgentOptions { ChatOptions = new ChatOptions { Tools = [new ApprovalRequiredAIFunction(ping)] } });
        AgentSession session = await guarded.CreateSessionAsync();
        AgentResponse asked = await guarded.RunAsync("ping", session);
        ToolApprovalRequestContent request = Assert.Single(asked.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Equal(1, pings);

        AgentResponse approved = await guarded.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);
        Assert.Equal(2, pings);
        Assert.Equal("pinged again", approved.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextMessageInsideTheClient_ReachesEveryModelCall_ButNotStoredHistory(bool streaming)
    {
        int pings = 0;
        AIFunction ping = AIFunctionFactory.Create(() => ++pings, "Ping");
        ScriptedChatClient model = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "Ping")]),
            new ChatMessage(ChatRole.Assistant, "done"));
        ChatClientAgent agent = new(new MarkingClient(model), new ChatClientAgentOptions { ChatOptions = new ChatOptions { Tools = [ping] } });
        AgentSession session = await agent.CreateSessionAsync();

        if (streaming)
        {
            await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync("go", session)) { }
        }
        else
        {
            await agent.RunAsync("go", session);
        }

        Assert.Equal(1, pings);
        Assert.Equal(2, model.Requests.Count);
        Assert.All(model.Requests, request => Assert.Equal(Marker, request[^1].Text));
        Assert.True(session.TryGetInMemoryChatHistory(out List<ChatMessage>? stored));
        Assert.NotEmpty(stored!);
        Assert.DoesNotContain(stored!, message => message.Text == Marker);
    }

    [Fact]
    public async Task ServiceStoredHistory_ConversationIdReachesTheInnerClient()
    {
        ScriptedChatClient model = new(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "one")) { ConversationId = "conv-1" },
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "two")) { ConversationId = "conv-1" });
        MarkingClient marking = new(model);
        ChatClientAgent agent = new(marking, new ChatClientAgentOptions());
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("first", session);
        await agent.RunAsync("second", session);

        Assert.Equal([null, "conv-1"], marking.ConversationIds);
    }

    private static List<IChatClient> Chain(IChatClient client)
    {
        PropertyInfo inner = typeof(DelegatingChatClient).GetProperty("InnerClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        List<IChatClient> chain = [client];
        while (chain[^1] is DelegatingChatClient delegating)
        {
            chain.Add((IChatClient)inner.GetValue(delegating)!);
        }
        return chain;
    }

    private static int IndexOf<T>(List<IChatClient> chain) => chain.FindIndex(client => client is T);

    private static void Log(List<IChatClient> chain) =>
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(" -> ", chain.Select(client => client.GetType().Name)));

    /// <summary>Appends a marked user message to every request and records the conversation id it was given.</summary>
    private sealed class MarkingClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        public List<string?> ConversationIds { get; } = [];

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            ConversationIds.Add(options?.ConversationId);
            return base.GetResponseAsync([.. messages, new ChatMessage(ChatRole.User, Marker)], options, cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            ConversationIds.Add(options?.ConversationId);
            return base.GetStreamingResponseAsync([.. messages, new ChatMessage(ChatRole.User, Marker)], options, cancellationToken);
        }
    }
}
