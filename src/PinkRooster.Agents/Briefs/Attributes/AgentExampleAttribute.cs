// Declared in the package's root namespace by hand, like CreateAgent: an agent class needs the root using alone for its attributes.
namespace PinkRooster.Agents;

/// <summary>Adds an input and output example to the system prompt of a <see cref="DeclaredAgent"/> class.</summary>
/// <remarks>A class may hold several. On a class hierarchy the base types' come first, then the derived class's.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class AgentExampleAttribute : Attribute
{
    /// <summary>The example input.</summary>
    public string Input { get; }

    /// <summary>The output the agent should give for it.</summary>
    public string Output { get; }

    /// <param name="input">What the user writes; must be non-blank.</param>
    /// <param name="output">What the agent answers; must be non-blank.</param>
    public AgentExampleAttribute(string input, string output)
    {
        Input = input;
        Output = output;
    }
}
