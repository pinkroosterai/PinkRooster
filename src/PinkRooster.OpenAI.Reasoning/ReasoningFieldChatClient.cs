using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

// JsonPatch is the SDK's only way to read a field it has no property for; it is marked experimental (SCME0001).
#pragma warning disable SCME0001

namespace PinkRooster.OpenAI.Reasoning;

/// <summary>
/// Adds the <c>reasoning</c> field that Ollama and Groq send for reasoning models as <see cref="TextReasoningContent"/>, which
/// <c>Microsoft.Extensions.AI.OpenAI</c> 10.10.0 drops: it reads only <c>reasoning_content</c>.
/// </summary>
/// <remarks>
/// It adds nothing to a response or update that already carries reasoning, so an adapter that reads the field itself is not doubled.
/// </remarks>
public sealed class ReasoningFieldChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<AIChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        AIChatMessage? answer = response.Messages.LastOrDefault(message => message.Role == ChatRole.Assistant);
        if (answer is not null
            && !response.Messages.Any(message => message.Contents.OfType<TextReasoningContent>().Any())
            && response.RawRepresentation is ChatCompletion completion
            && completion.Patch.TryGetValue("$.choices[0].message.reasoning"u8, out string? reasoning)
            && !string.IsNullOrEmpty(reasoning))
        {
            answer.Contents.Insert(0, new TextReasoningContent(reasoning));
        }
        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AIChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            // The final usage chunk has no choices, and reading a choice from it throws (openai-dotnet#1281).
            if (update.RawRepresentation is StreamingChatCompletionUpdate chunk
                && chunk.Usage is null
                && !update.Contents.OfType<TextReasoningContent>().Any()
                && chunk.Patch.TryGetValue("$.choices[0].delta.reasoning"u8, out string? reasoning)
                && !string.IsNullOrEmpty(reasoning))
            {
                update.Contents.Insert(0, new TextReasoningContent(reasoning));
            }
            yield return update;
        }
    }
}
