using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace PinkRooster.ToolCollections.Context;

/// <summary>Adds the current context of tool collections to every model call, as one marked message after the rest of the request.</summary>
/// <remarks>
/// <para>
/// Put it inside the client handed to an agent, so it runs below the agent's tool loop and history: each model call, tool
/// loop included, gets fresh context, the message never reaches stored history, and the system prompt stays the same between
/// calls. It needs no running agent, so it also works on a bare <see cref="IChatClient"/>.
/// </para>
/// <para>
/// When the request carries a <see cref="ChatOptions.ConversationId"/>, the service stores what it is sent, so the context goes
/// into that call's <see cref="ChatOptions.Instructions"/> instead of a message. It adds no tools: give the collections' tools to
/// the agent as well.
/// </para>
/// </remarks>
public sealed class ToolCollectionChatClient : DelegatingChatClient
{
    /// <summary>The <see cref="ChatMessage.AdditionalProperties"/> key that marks the context message, so an integrator can find it.</summary>
    public const string MarkerKey = "PinkRooster.ToolCollectionContext";

    /// <summary>The first line of the context message, so the model does not take it for something the user wrote.</summary>
    public const string FramingLine = ToolCollectionContext.FramingLine;

    private readonly ToolCollection[] collections;
    private readonly ILogger? logger;

    /// <param name="innerClient">The client that sends requests to the model.</param>
    /// <param name="collections">The collections whose context is sent, in this order.</param>
    /// <param name="logger">Where a failing collection is logged. With or without one the collection is left out of that model call and the call goes on; without one the failure is invisible.</param>
    public ToolCollectionChatClient(IChatClient innerClient, IEnumerable<ToolCollection> collections, ILogger? logger = null) : base(innerClient)
    {
        this.collections = ToolCollectionContext.Copy(collections, nameof(collections));
        this.logger = logger;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        string? context = await ToolCollectionContext.BuildAsync(collections, logger, cancellationToken).ConfigureAwait(false);
        (IEnumerable<ChatMessage> request, ChatOptions? requestOptions) = AddContext(messages, options, context);
        return await base.GetResponseAsync(request, requestOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? context = await ToolCollectionContext.BuildAsync(collections, logger, cancellationToken).ConfigureAwait(false);
        (IEnumerable<ChatMessage> request, ChatOptions? requestOptions) = AddContext(messages, options, context);
        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(request, requestOptions, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private static (IEnumerable<ChatMessage> Messages, ChatOptions? Options) AddContext(IEnumerable<ChatMessage> messages, ChatOptions? options, string? context)
    {
        if (context is null)
        {
            return (messages, options);
        }

        if (options?.ConversationId is not null)
        {
            ChatOptions withContext = options.Clone();
            withContext.Instructions = string.IsNullOrEmpty(options.Instructions) ? context : $"{options.Instructions}\n\n{context}";
            return (messages, withContext);
        }

        ChatMessage message = new(ChatRole.User, context) { AdditionalProperties = new() { [MarkerKey] = true } };
        return ([.. messages, message], options);
    }
}
