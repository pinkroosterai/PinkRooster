using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace PinkRooster.OpenAI.Reasoning.Tests;

/// <summary>Raw payloads carrying a <c>reasoning</c> field, shaped as Groq and Ollama sent them on 2026-09-25.</summary>
public sealed class ReasoningFieldChatClientTests
{
    private const string ReasoningChunk = """{"id":"c","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"reasoning":"91 = 7*13.","channel":"analysis"},"finish_reason":null}]}""";
    private const string ContentChunk = """{"id":"c","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"content":"No"},"finish_reason":null}]}""";
    private const string UsageChunk = """{"id":"c","object":"chat.completion.chunk","created":1,"model":"m","choices":[],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";
    private const string Completion = """{"id":"c","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"message":{"role":"assistant","content":"No","reasoning":"91 = 7*13."},"finish_reason":"stop"}]}""";

    [Fact]
    public async Task StreamedReasoningField_BecomesReasoningContent_AndTheUsageChunkPassesThrough()
    {
        RawStreamClient inner = new(
            Update(ReasoningChunk),
            Update(ContentChunk, new TextContent("No")),
            Update(UsageChunk));

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in new ReasoningFieldChatClient(inner).GetStreamingResponseAsync([], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        Assert.Equal("91 = 7*13.", Assert.IsType<TextReasoningContent>(Assert.Single(updates[0].Contents)).Text);
        Assert.IsType<TextContent>(Assert.Single(updates[1].Contents));
        Assert.Empty(updates[2].Contents);
    }

    [Fact]
    public async Task StreamedUpdateThatAlreadyCarriesReasoning_IsNotDoubled()
    {
        RawStreamClient inner = new(Update(ReasoningChunk, new TextReasoningContent("from the adapter")));

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in new ReasoningFieldChatClient(inner).GetStreamingResponseAsync([], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        Assert.Equal("from the adapter", Assert.IsType<TextReasoningContent>(Assert.Single(updates[0].Contents)).Text);
    }

    [Fact]
    public async Task ReasoningFieldOfAPlainResponse_LeadsTheAnswer()
    {
        ChatResponse raw = new(new AIChatMessage(ChatRole.Assistant, "No")) { RawRepresentation = ModelReaderWriter.Read<ChatCompletion>(BinaryData.FromString(Completion)) };

        ChatResponse response = await new ReasoningFieldChatClient(new RawStreamClient(raw)).GetResponseAsync([], cancellationToken: TestContext.Current.CancellationToken);

        AIContent[] contents = [.. Assert.Single(response.Messages).Contents];
        Assert.Equal("91 = 7*13.", Assert.IsType<TextReasoningContent>(contents[0]).Text);
        Assert.Equal("No", Assert.IsType<TextContent>(contents[1]).Text);
    }

    private static ChatResponseUpdate Update(string json, params AIContent[] contents) =>
        new(ChatRole.Assistant, [.. contents]) { RawRepresentation = ModelReaderWriter.Read<StreamingChatCompletionUpdate>(BinaryData.FromString(json)) };

    /// <summary>Returns the given raw-backed response or updates, as the OpenAI adapter would.</summary>
    private sealed class RawStreamClient : IChatClient
    {
        private readonly ChatResponse? response;
        private readonly ChatResponseUpdate[] updates = [];

        public RawStreamClient(ChatResponse response) => this.response = response;

        public RawStreamClient(params ChatResponseUpdate[] updates) => this.updates = updates;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<AIChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(response ?? throw new InvalidOperationException("No response scripted."));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AIChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ChatResponseUpdate update in updates)
            {
                await Task.Yield();
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
