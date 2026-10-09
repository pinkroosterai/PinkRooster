using PinkRooster.Agents.Shared;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tools;

/// <summary>Creates the tool collections an agent class names with <see cref="AgentToolsAttribute"/>.</summary>
internal static class AgentClassCollections
{
    /// <summary>A new object for each collection type the class and its base types name, base types first.</summary>
    /// <exception cref="InvalidOperationException">A type is null, is not a tool collection, or has no public parameterless constructor; the message names the class and the fix.</exception>
    public static List<ToolCollection> Create(Type type)
    {
        List<Type> chain = TypeChain.BaseFirst(type);

        List<ToolCollection> collections = [];
        foreach (Type current in chain)
        {
            foreach (AgentToolsAttribute attribute in current.GetCustomAttributes(typeof(AgentToolsAttribute), inherit: false).Cast<AgentToolsAttribute>())
            {
                string where = current == type ? $"agent class '{type.Name}'" : $"'{current.Name}', a base of agent class '{type.Name}'";
                if (attribute.Collections.Length == 0)
                {
                    throw new InvalidOperationException($"An [AgentTools] on {where} names no tool collection; name one, or remove the attribute.");
                }
                foreach (Type? collection in attribute.Collections)
                {
                    collections.Add(Instantiate(collection, where));
                }
            }
        }
        return collections;
    }

    /// <summary>Creates the collections the class and its base types name with a <see cref="ToolCollectionSourceAttribute"/>, base types first.</summary>
    /// <remarks>A collection created before a later one fails is disposed, so a failed build leaves no open connection.</remarks>
    public static async Task<List<ToolCollection>> CreateSourcedAsync(Type type, CancellationToken cancellationToken)
    {
        List<Type> chain = TypeChain.BaseFirst(type);

        List<ToolCollection> collections = [];
        try
        {
            foreach (Type current in chain)
            {
                foreach (ToolCollectionSourceAttribute source in current.GetCustomAttributes(typeof(ToolCollectionSourceAttribute), inherit: false).Cast<ToolCollectionSourceAttribute>())
                {
                    collections.Add(await source.CreateAsync(cancellationToken).ConfigureAwait(false));
                }
            }
        }
        catch
        {
            await DisposeAsync(collections).ConfigureAwait(false);
            throw;
        }
        return collections;
    }

    /// <summary>Disposes each collection that can be disposed.</summary>
    public static async ValueTask DisposeAsync(IEnumerable<ToolCollection> collections)
    {
        foreach (ToolCollection collection in collections)
        {
            switch (collection)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    private static ToolCollection Instantiate(Type? collection, string where)
    {
        if (collection is null)
        {
            throw new InvalidOperationException($"An [AgentTools] on {where} names a null type; name a tool collection class.");
        }
        if (!typeof(ToolCollection).IsAssignableFrom(collection) || collection.IsAbstract)
        {
            throw new InvalidOperationException($"[AgentTools] on {where} names '{collection.Name}', which is not a concrete ToolCollection; name a class derived from ToolCollection.");
        }
        if (collection.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException($"[AgentTools] on {where} names '{collection.Name}', which has no public parameterless constructor; add one, or call WithTools(new {collection.Name}(...)) in Configure.");
        }
        return (ToolCollection)Activator.CreateInstance(collection)!;
    }
}
