// Declared in the package's root namespace by hand, like CreateAgent: an agent class needs the root using alone for its attributes.
namespace PinkRooster.Agents;

/// <summary>Lets the model start the named tools of a <see cref="DeclaredAgent"/> class in the background.</summary>
/// <remarks>
/// <para>
/// It is the attribute form of the builder's <c>AllowBackground</c>, for the class's own tools and for tools of the collections named with
/// <see cref="AgentToolsAttribute"/> or added in <c>Configure</c>. Names are matched ignoring case, when the agent is built, and a name no tool has
/// fails the build. Naming a tool says it may run at the same time as other calls on the same collection instance.
/// </para>
/// <para>A class may hold several. On a class hierarchy the base types' names come first, then the derived class's.</para>
/// </remarks>
/// <example>
/// <code>
/// [AgentTools(typeof(ShellToolCollection))]
/// [AgentAllowBackground("RunShell")]
/// public sealed class BuildAgent(IChatClient chatClient) : DeclaredAgent(chatClient);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class AgentAllowBackgroundAttribute : Attribute
{
    /// <summary>The tool names.</summary>
    public string[] ToolNames { get; }

    /// <param name="toolNames">One or more tool names; each must be non-blank.</param>
    public AgentAllowBackgroundAttribute(params string[] toolNames)
    {
        ToolNames = toolNames ?? [];
    }
}
