using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

/// <summary>A chat client that never answers: it throws the given exception, or waits until it is cancelled, and counts the requests.</summary>
internal sealed class UnansweringChatClient(Exception? failure = null) : IChatClient
{
    public int Requests { get; private set; }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests++;
        if (failure is not null)
        {
            throw failure;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Not reached.");
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
