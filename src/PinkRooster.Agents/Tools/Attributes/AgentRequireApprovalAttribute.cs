// Declared in the package's root namespace by hand, like CreateAgent: an agent class needs the root using alone for its attributes.
namespace PinkRooster.Agents;

/// <summary>Makes the named tools of a <see cref="DeclaredAgent"/> class need the host's approval before they run.</summary>
/// <remarks>
/// <para>
/// It is the attribute form of the builder's <c>RequireApproval</c>, for tools of the collections named with <see cref="AgentToolsAttribute"/> or added in
/// <c>Configure</c>. The class's own tools are marked with <c>RequiresApproval</c> on their <c>[Tool]</c> attribute. Names are matched ignoring case, when the agent is built,
/// and a name no tool has fails the build.
/// </para>
/// <para>A class may hold several. On a class hierarchy the base types' names come first, then the derived class's.</para>
/// </remarks>
/// <example>
/// <code>
/// [AgentTools(typeof(ShellToolCollection))]
/// [AgentRequireApproval("RunShell")]
/// public sealed class BuildAgent(IChatClient chatClient) : DeclaredAgent(chatClient);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class AgentRequireApprovalAttribute : Attribute
{
    /// <summary>The tool names.</summary>
    public string[] ToolNames { get; }

    /// <param name="toolNames">One or more tool names; each must be non-blank.</param>
    public AgentRequireApprovalAttribute(params string[] toolNames)
    {
        ToolNames = toolNames ?? [];
    }
}
