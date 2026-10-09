using Microsoft.Agents.AI;

namespace PinkRooster.Samples.CodingAgent.Skills;

/// <summary>
/// The skills of the workspace: every folder under <c>.agents/skills/</c> that holds a <c>SKILL.md</c>. MAF's
/// <see cref="AgentSkillsProvider"/> puts each skill's name and description in the system prompt and gives the agent a tool that
/// loads a skill's body when it needs it, so a skill costs a line until it is used.
/// </summary>
public static class WorkspaceSkills
{
    public const string Folder = ".agents/skills";

    /// <summary>The provider for the workspace's skills, or null when it has no skills folder. No script runner is given, so a skill's scripts are not run.</summary>
    public static AgentSkillsProvider? Find(string workspaceRoot, out int count)
    {
        string folder = Path.Combine(workspaceRoot, Folder.Replace('/', Path.DirectorySeparatorChar));
        count = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "SKILL.md", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 }).Count()
            : 0;
        return count == 0 ? null : new AgentSkillsProvider(folder);
    }
}
