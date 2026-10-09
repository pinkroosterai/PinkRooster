using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections;

/// <summary>
/// A tool collection made from functions defined elsewhere, such as generated ones or another SDK's, given the same standing
/// instructions, constraints, approvals and live context as a hand-written <see cref="ToolCollection"/>.
/// </summary>
/// <remarks>
/// The members and their rules are those of <see cref="ExternalToolCollection{TSelf}"/>. For a collection with members of its own,
/// derive from that type instead of this one.
/// </remarks>
public sealed class ExternalToolCollection : ExternalToolCollection<ExternalToolCollection>
{
    /// <param name="name">How messages name the collection, such as the server it came from.</param>
    /// <param name="tools">The tools, in the order the model sees them. Filter or rename them before passing them in.</param>
    /// <exception cref="ArgumentException">The name is blank, the list holds a null entry, or two tools share a name, ignoring case.</exception>
    public ExternalToolCollection(string name, IEnumerable<AIFunction> tools) : base(name, tools)
    {
    }
}
