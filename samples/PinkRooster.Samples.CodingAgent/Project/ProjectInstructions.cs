namespace PinkRooster.Samples.CodingAgent.Project;

/// <summary>The project's instruction file, <c>AGENTS.md</c> in the workspace root: what the team wants every coding agent to know.</summary>
public static class ProjectInstructions
{
    public const string FileName = "AGENTS.md";

    /// <summary>The whole file's text, or null when there is none or it is empty. The file is read at start and at <c>/clear</c>.</summary>
    public static string? Read(string workspaceRoot)
    {
        string path = Path.Combine(workspaceRoot, FileName);
        if (!File.Exists(path))
        {
            return null;
        }
        string text = File.ReadAllText(path);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
