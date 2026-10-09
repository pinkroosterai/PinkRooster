using PinkRooster.Agents.Prompts;

// Declared in the package's root namespace by hand, like CreateAgent: an agent class needs the root using alone for its attributes.
namespace PinkRooster.Agents;

/// <summary>Leaves <see cref="AgentDefaults"/> out of the system prompt of a <see cref="DeclaredAgent"/> class: the attribute form of the builder's <c>WithoutDefaults()</c>.</summary>
/// <remarks>
/// A tool collection's own text still goes in. On a class hierarchy it applies to every class derived from the one that holds it. A <c>WithDefaults</c> call in
/// <c>Configure</c> runs after it and wins.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AgentWithoutDefaultsAttribute : Attribute;
