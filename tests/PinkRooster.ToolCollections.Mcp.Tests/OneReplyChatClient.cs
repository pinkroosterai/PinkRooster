using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections.Mcp.Tests;

/// <summary>Answers every request with the same text, never calls a tool, and keeps what the last request carried.</summary>
internal sealed class OneReplyChatClient(string reply) : IChatClient
{
    public string? Instructions { get; private set; }

    public List<string> ToolNames { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Instructions = options?.Instructions;
        ToolNames.Clear();
        ToolNames.AddRange((options?.Tools ?? []).Select(tool => tool.Name));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
