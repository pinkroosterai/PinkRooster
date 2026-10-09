using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.ToolCollections.Context;
using static PinkRooster.ToolCollections.Tests.TestCollections;

namespace PinkRooster.ToolCollections.Tests.Context;

public sealed class ToolCollectionChatClientTests
{
    private const string Framing = ToolCollectionChatClient.FramingLine;

    [Fact]
    public async Task AToolCallChangesTheContextOfTheNextModelCallAndLeavesEverythingBeforeItUnchanged()
    {
        ScriptedChatClient client = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Increment", new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = AgentWith(client, new Counter());

        await agent.RunAsync("go");

        List<ChatMessage> first = client.Requests[0];
        List<ChatMessage> second = client.Requests[1];
        Assert.Equal($"{Framing}\n\n## Counter\n0", first[^1].Text);
        Assert.Equal($"{Framing}\n\n## Counter\n1", second[^1].Text);
        Assert.Equal(ChatRole.User, second[^1].Role);
        // The second call is the first without its context, then the tool call and its result, then fresh context.
        Assert.Equal(first.Count + 2, second.Count);
        for (int i = 0; i < first.Count - 1; i++)
        {
            Assert.Same(first[i], second[i]);
        }
        Assert.Contains(second[^3].Contents, content => content is FunctionCallContent);
        Assert.Contains(second[^2].Contents, content => content is FunctionResultContent);
    }

    [Fact]
    public async Task OnlyTheContextMessageCarriesTheMarker()
    {
        ScriptedChatClient client = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Increment", new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = AgentWith(client, new Counter());

        await agent.RunAsync("go");

        foreach (List<ChatMessage> request in client.Requests)
        {
            Assert.True(IsContext(request[^1]));
            Assert.DoesNotContain(request.Take(request.Count - 1), IsContext);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASessionStoresNoToolContextAndKeepsItsPrefixInstructionsAndToolsStable(bool streaming)
    {
        ScriptedChatClient client = new(
            new ChatMessage(ChatRole.Assistant, "one"),
            new ChatMessage(ChatRole.Assistant, "two"),
            new ChatMessage(ChatRole.Assistant, "three"));
        AIAgent agent = AgentWith(client, new Counter(), instructions: "Be brief.");
        AgentSession session = await agent.CreateSessionAsync();

        foreach (string input in new[] { "a", "b", "c" })
        {
            await RunAsync(agent, input, session, streaming);
        }

        Assert.True(session.TryGetInMemoryChatHistory(out List<ChatMessage>? stored));
        Assert.Equal(6, stored!.Count);
        Assert.DoesNotContain(stored, IsContext);
        for (int call = 1; call < client.Requests.Count; call++)
        {
            List<ChatMessage> previous = client.Requests[call - 1];
            List<ChatMessage> current = client.Requests[call];
            // Everything the previous call sent before its context comes first again, with the same text.
            Assert.Equal(previous.Take(previous.Count - 1).Select(message => message.Text), current.Take(previous.Count - 1).Select(message => message.Text));
            Assert.True(IsContext(current[^1]));
            Assert.Equal(client.Options[0]!.Instructions, client.Options[call]!.Instructions);
            Assert.Equal(client.Options[0]!.Tools!.Select(tool => tool.Name), client.Options[call]!.Tools!.Select(tool => tool.Name));
        }
    }

    [Fact]
    public async Task CollectionsWithoutContextAddNoMessage()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = AgentWith(client, new Silent());

        await agent.RunAsync("go");

        ChatMessage only = Assert.Single(client.Requests[0]);
        Assert.Equal("go", only.Text);
    }

    [Fact]
    public async Task ContextFromSeveralCollectionsGoesInOneMessageInTheOrderAdded()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = AgentWith(client, new Counter(), new Silent(), new Note("## Note\nremember"));

        await agent.RunAsync("go");

        Assert.Equal($"{Framing}\n\n## Counter\n0\n\n## Note\nremember", Assert.Single(client.Requests[0], IsContext).Text);
    }

    [Fact]
    public async Task AFailingCollectionIsLoggedAndSkippedWhileTheOthersStillSendContext()
    {
        RecordingLogger logger = new();
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = AgentWith(client, logger, new Failing(), new Counter());

        AgentResponse response = await agent.RunAsync("go");

        Assert.Equal("done", response.Text);
        Assert.Equal($"{Framing}\n\n## Counter\n0", client.Requests[0][^1].Text);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task AFailingCollection_IsLeftOutOfTheModelCall_AndTheRunGoesOn_WhenThereIsNoLogger()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = AgentWith(client, new Failing());

        AgentResponse response = await agent.RunAsync("go");

        Assert.Equal("done", response.Text);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task CancellationDuringContextPropagatesEvenWithALogger()
    {
        RecordingLogger logger = new();
        using CancellationTokenSource cancellation = new();
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = AgentWith(client, logger, new Cancelling(cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agent.RunAsync("go", cancellationToken: cancellation.Token));

        Assert.Empty(logger.Errors);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task WithServiceStoredHistory_ContextGoesInThatCallsInstructionsInsteadOfAMessage()
    {
        ScriptedChatClient client = new(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "one")) { ConversationId = "conv-1" },
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "two")) { ConversationId = "conv-1" });
        AIAgent agent = AgentWith(client, new Counter(), instructions: "Be brief.");
        AgentSession session = await agent.CreateSessionAsync();

        await agent.RunAsync("a", session);
        await agent.RunAsync("b", session);

        // The first call has no conversation yet, so it gets the message; the second is service-stored.
        Assert.True(IsContext(client.Requests[0][^1]));
        Assert.DoesNotContain(client.Requests[1], IsContext);
        Assert.Equal($"Be brief.\n\n{Framing}\n\n## Counter\n0", client.Options[1]!.Instructions);
    }

    [Fact]
    public async Task WorksOnABareChatClientWithoutAnAgent()
    {
        ScriptedChatClient model = new(new ChatMessage(ChatRole.Assistant, "done"));
        IChatClient client = model.AsBuilder().UseToolCollections(new Counter()).Build();

        await client.GetResponseAsync("go");

        Assert.Equal($"{Framing}\n\n## Counter\n0", model.Requests[0][^1].Text);
    }

    [Fact]
    public void RejectsANullCollection()
    {
        Assert.Throws<ArgumentException>(() => new ToolCollectionChatClient(new ScriptedChatClient(Array.Empty<ChatMessage>()), [new Counter(), null!]));
    }

    private static AIAgent AgentWith(IChatClient model, params ToolCollection[] collections) => AgentWith(model, null, collections);

    private static AIAgent AgentWith(IChatClient model, RecordingLogger? logger, params ToolCollection[] collections) =>
        AgentWith(model, collections, instructions: null, logger);

    private static AIAgent AgentWith(IChatClient model, ToolCollection collection, string instructions) =>
        AgentWith(model, [collection], instructions, logger: null);

    private static AIAgent AgentWith(IChatClient model, ToolCollection[] collections, string? instructions, RecordingLogger? logger) =>
        new ChatClientAgent(
            new ToolCollectionChatClient(model, collections, logger),
            new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    Tools = [.. collections.SelectMany(collection => collection.GetAIFunctions())]
                }
            });

    private static async Task RunAsync(AIAgent agent, string input, AgentSession session, bool streaming)
    {
        if (streaming)
        {
            await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync(input, session)) { }
        }
        else
        {
            await agent.RunAsync(input, session);
        }
    }

    private static bool IsContext(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue(ToolCollectionChatClient.MarkerKey, out object? value) == true && value is true;
}
