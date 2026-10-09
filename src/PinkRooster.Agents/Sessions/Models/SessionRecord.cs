namespace PinkRooster.Agents.Sessions;

/// <summary>What a <see cref="SessionStore"/> keeps in a session about its file, so a session is saved to the same file every time, also after it was restored.</summary>
internal sealed class SessionRecord
{
    public string Id { get; set; } = string.Empty;

    public string FirstPrompt { get; set; } = string.Empty;
}
