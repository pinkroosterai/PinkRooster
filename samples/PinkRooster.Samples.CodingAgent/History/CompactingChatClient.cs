using Microsoft.Extensions.AI;

namespace PinkRooster.Samples.CodingAgent.History;

/// <summary>
/// Client middleware that sends each request as <see cref="Compaction"/> shortens it. Added with <c>ConfigureClient</c>, it sits
/// below MAF's history persistence, where a request is the whole saved history and the new input, so the history itself stays whole.
/// </summary>
public sealed class CompactingChatClient(IChatClient innerClient, Compaction compaction) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        await base.GetResponseAsync(await compaction.ShortenAsync(messages, cancellationToken).ConfigureAwait(false), options, cancellationToken).ConfigureAwait(false);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IEnumerable<ChatMessage> shortened = await compaction.ShortenAsync(messages, cancellationToken).ConfigureAwait(false);
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(shortened, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }
}
