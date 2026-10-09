using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace PinkRooster.ToolCollections.Context;

/// <summary>Adds a <see cref="ToolCollectionChatClient"/> to a <see cref="ChatClientBuilder"/> pipeline.</summary>
public static class ToolCollectionChatClientBuilderExtensions
{
    /// <summary>Adds the collections' current context to every model call made through the pipeline.</summary>
    /// <remarks>
    /// The context client logs through the <see cref="ILoggerFactory"/> in the builder's services, if any. It adds no tools:
    /// give the collections' tools to the agent or the request as well.
    /// </remarks>
    public static ChatClientBuilder UseToolCollections(this ChatClientBuilder builder, params IEnumerable<ToolCollection> collections)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ToolCollection[] copy = ToolCollectionContext.Copy(collections, nameof(collections));
        return builder.Use((inner, services) =>
            new ToolCollectionChatClient(inner, copy, (services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger<ToolCollectionChatClient>()));
    }
}
