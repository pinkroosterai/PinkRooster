using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Tests.TestSupport;

/// <summary>Records every request; <c>reply</c> gets the zero-based turn number within a run.</summary>
internal sealed class RecordingChatClient(Func<int, string> reply, Func<Task>? beforeReply = null) : IChatClient
{
    private readonly object gate = new();

    public List<RecordedRequest> Requests { get; } = [];

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        List<string> texts = [.. messages.Select(message => message.Text)];
        List<string> toolNames = [.. (options?.Tools ?? []).Select(tool => tool.Name)];
        lock (gate) Requests.Add(new RecordedRequest(texts, options?.ResponseFormat, options?.Instructions, toolNames));
        if (beforeReply is not null) await beforeReply();
        // Each turn adds a user message and a reply, so the history length tells which call of a run this is.
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply((texts.Count - 1) / 2)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
