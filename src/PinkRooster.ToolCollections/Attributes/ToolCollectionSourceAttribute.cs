namespace PinkRooster.ToolCollections;

/// <summary>
/// Base for an attribute that gives an agent class a tool collection that has to be created asynchronously, such as one that connects to a
/// server first. A collection with a public parameterless constructor does not need it: name its type in <c>[AgentTools]</c>.
/// </summary>
/// <remarks>
/// The agent calls <see cref="CreateAsync"/> once per instance, when it is built, and disposes what it returned when the agent is disposed,
/// if that implements <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public abstract class ToolCollectionSourceAttribute : Attribute
{
    /// <summary>Creates the collection for one agent instance.</summary>
    /// <param name="cancellationToken">Cancels creating the collection.</param>
    /// <returns>A new collection, owned by the agent.</returns>
    public abstract ValueTask<ToolCollection> CreateAsync(CancellationToken cancellationToken);
}
