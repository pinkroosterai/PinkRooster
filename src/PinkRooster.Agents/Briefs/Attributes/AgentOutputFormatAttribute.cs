// Declared in the package's root namespace by hand, like CreateAgent: an agent class needs the root using alone for its attributes.
namespace PinkRooster.Agents;

/// <summary>Sets the OutputFormat section of a <see cref="DeclaredAgent"/> class: the shape the agent's response must take.</summary>
/// <remarks>
/// A class holds one. On a class hierarchy, the one on the derived class replaces the base class's. Several strings are joined with
/// newlines. The text is constant: text that depends on a constructor argument is set in <c>Configure</c>, with the builder's own method.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AgentOutputFormatAttribute : Attribute
{
    /// <summary>The text, one entry per line.</summary>
    public string[] Lines { get; }

    /// <param name="lines">One or more lines of text; each must be non-blank.</param>
    public AgentOutputFormatAttribute(params string[] lines)
    {
        Lines = lines ?? [];
    }
}
