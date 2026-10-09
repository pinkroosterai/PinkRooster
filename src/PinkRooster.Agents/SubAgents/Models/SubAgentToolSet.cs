using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.SubAgents;

/// <summary>A named group of tools the calling model can give to a sub-agent.</summary>
/// <param name="Name">The name the calling model picks it by, such as <c>files</c>.</param>
/// <param name="UseWhen">When to pick it, in a line the calling model reads, such as <c>Reading and searching the repository.</c></param>
public sealed record SubAgentToolSet(string Name, string UseWhen)
{
    /// <summary>The tool collections of the set. An instance is shared by every sub-agent that gets the set.</summary>
    public IReadOnlyList<ToolCollection> Collections { get; init; } = [];

    /// <summary>The plain tools of the set, such as another agent's <c>AsAIFunction()</c>.</summary>
    public IReadOnlyList<AITool> Tools { get; init; } = [];
}
