using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace PinkRooster.Samples.Models;

/// <summary>Counts the tool calls the model asked for, so a sample can say when it asked for none.</summary>
internal sealed class ToolCallWatchingChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private int toolCalls;

    /// <summary>How many tool calls the model asked for so far, in responses and in streamed updates.</summary>
    public int ToolCalls => Volatile.Read(ref toolCalls);

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        Count(response.Messages.SelectMany(message => message.Contents));
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            Count(update.Contents);
            yield return update;
        }
    }

    private void Count(IEnumerable<AIContent> contents) => Interlocked.Add(ref toolCalls, contents.OfType<FunctionCallContent>().Count());
}
