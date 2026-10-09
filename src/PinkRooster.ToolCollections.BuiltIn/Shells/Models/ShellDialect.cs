namespace PinkRooster.ToolCollections.BuiltIn.Shells;

/// <summary>How <see cref="PinkRooster.ToolCollections.BuiltIn.Shells.ShellToolCollection" /> hands a command to a <see cref="Shell" />.</summary>
public enum ShellDialect
{
    /// <summary>
    /// Windows PowerShell or PowerShell 7: <c>-NoProfile -NonInteractive -EncodedCommand</c>, with progress off, UTF-8
    /// output, and the last native exit code passed through.
    /// </summary>
    PowerShell,

    /// <summary>A POSIX shell such as sh or bash: <c>-c &lt;command&gt;</c>.</summary>
    Posix
}
