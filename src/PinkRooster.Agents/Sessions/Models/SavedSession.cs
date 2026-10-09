namespace PinkRooster.Agents.Sessions;

/// <summary>One session file of a <see cref="SessionStore"/>, as its list shows it.</summary>
/// <param name="Id">The name the session is restored by; the file's name without <c>.json</c>.</param>
/// <param name="FirstPrompt">The prompt of the first run that was saved, for a line a user recognises the conversation by; empty for a file that cannot be read.</param>
/// <param name="SavedAt">When the file was last written.</param>
/// <param name="Tools">The names of the agent's tools when the session was last saved, in the agent's order.</param>
/// <param name="FilePath">The file.</param>
public sealed record SavedSession(string Id, string FirstPrompt, DateTimeOffset SavedAt, IReadOnlyList<string> Tools, string FilePath)
{
    /// <summary>Why the file cannot be read; null for a session that can be restored. The store lists such a file and leaves it where it is.</summary>
    public string? Problem { get; init; }

    /// <summary>False for a file that cannot be read: show it as unreadable, and do not offer it.</summary>
    public bool IsReadable => Problem is null;
}
