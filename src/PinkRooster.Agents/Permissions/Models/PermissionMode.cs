namespace PinkRooster.Agents.Permissions;

/// <summary>How a <see cref="PermissionPolicy"/> answers a tool call that needs approval and that no allow rule covers.</summary>
public enum PermissionMode
{
    /// <summary>The user is asked about every call that edits, executes or declares no kind.</summary>
    Ask,

    /// <summary>A call that edits runs unasked; the user is still asked about one that executes or declares no kind.</summary>
    AcceptEdits,

    /// <summary>
    /// Nothing is changed and nobody is asked: a call that edits is refused, and one that executes or declares no kind runs only
    /// when a rule given with <see cref="PermissionPolicy.Allow"/> covers it.
    /// </summary>
    Plan
}
