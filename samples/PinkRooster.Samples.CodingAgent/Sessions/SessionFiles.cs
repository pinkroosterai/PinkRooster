using PinkRooster.Agents.Sessions;

namespace PinkRooster.Samples.CodingAgent.Sessions;

/// <summary>Where the conversations of a workspace are kept: one file per conversation under <c>.pinkrooster/sessions</c>, written by the library's session store.</summary>
public static class SessionFiles
{
    /// <summary>The store for <paramref name="workspaceRoot"/>.</summary>
    public static SessionStore For(string workspaceRoot)
    {
        string scratch = Path.Combine(workspaceRoot, ".pinkrooster");
        // A session file holds the whole conversation. The built-in tools ignore this folder in git when they first use it;
        // the store may be first, so the same ignore file is written here.
        try
        {
            Directory.CreateDirectory(scratch);
            string ignore = Path.Combine(scratch, ".gitignore");
            if (!File.Exists(ignore))
            {
                File.WriteAllText(ignore, "*\n");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A workspace that cannot be written to still runs; saving the session says so after the first run.
        }
        return new SessionStore(Path.Combine(scratch, "sessions"));
    }

    /// <summary>One line a user recognises a saved conversation by.</summary>
    public static string Describe(SavedSession saved)
    {
        string prompt = string.Join(" ", saved.FirstPrompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"{saved.SavedAt.ToLocalTime():yyyy-MM-dd HH:mm}  {(prompt.Length <= 60 ? prompt : prompt[..60] + "…")}";
    }
}
