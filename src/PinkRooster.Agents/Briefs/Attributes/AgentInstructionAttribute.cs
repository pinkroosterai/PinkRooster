// Declared in the package's root namespace by hand, like CreateAgent: an agent class needs the root using alone for its attributes.
namespace PinkRooster.Agents;

/// <summary>Adds instructions to the system prompt of a <see cref="DeclaredAgent"/> class.</summary>
/// <remarks>
/// A class may hold several. On a class hierarchy the base types' come first, then the derived class's. The order between several of
/// these attributes on one class isn't guaranteed, so put text whose order matters in one attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class AgentInstructionAttribute : Attribute
{
    /// <summary>The instructions, in order.</summary>
    public string[] Instructions { get; }

    /// <param name="instructions">One or more instructions; each must be non-blank.</param>
    public AgentInstructionAttribute(params string[] instructions)
    {
        Instructions = instructions ?? [];
    }
}
