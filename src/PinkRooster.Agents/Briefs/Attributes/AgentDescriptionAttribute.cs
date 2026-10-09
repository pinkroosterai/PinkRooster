// Declared in the package's root namespace by hand, like CreateAgent: an agent class needs the root using alone for its attributes.
namespace PinkRooster.Agents;

/// <summary>Sets the description of a <see cref="DeclaredAgent"/> class: what the agent does, for whoever chooses it.</summary>
/// <remarks>
/// <para>
/// A model that is given the agent as a tool through <c>AsAIFunction()</c> reads this text to decide when to call it. It is not part of the agent's own
/// system prompt. A class holds one. On a class hierarchy, the one on the derived class replaces the base class's.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AgentDescriptionAttribute : Attribute
{
    /// <summary>The description.</summary>
    public string Description { get; }

    /// <param name="description">The description; it must be non-blank.</param>
    public AgentDescriptionAttribute(string description)
    {
        Description = description;
    }
}
