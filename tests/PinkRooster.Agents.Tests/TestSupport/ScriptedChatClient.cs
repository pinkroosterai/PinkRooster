using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Tests.TestSupport;

/// <summary>Returns the given responses in order, one per request, and records every request; streaming yields the same responses as updates.</summary>
internal sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
{
    private readonly object gate = new();

    public ScriptedChatClient(params ChatMessage[] responses) : this([.. responses.Select(message => new ChatResponse(message))])
    {
    }

    public List<List<ChatMessage>> Requests { get; } = [];

    /// <summary>The options of each request, in the same order as <see cref="Requests"/>.</summary>
    public List<ChatOptions?> Options { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Next(messages, options));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (ChatResponseUpdate update in Next(messages, options).ToChatResponseUpdates())
        {
            await Task.Yield();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    private ChatResponse Next(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        lock (gate)
        {
            Requests.Add([.. messages]);
            Options.Add(options?.Clone());
            if (Requests.Count > responses.Length)
            {
                throw new InvalidOperationException($"The script has {responses.Length} responses; request {Requests.Count} has none.");
            }
            return responses[Requests.Count - 1];
        }
    }
}
