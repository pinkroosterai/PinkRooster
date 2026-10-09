namespace PinkRooster.ToolCollections.BuiltIn.Shells;

/// <summary>
/// The shell <see cref="PinkRooster.ToolCollections.BuiltIn.Shells.ShellToolCollection" /> runs commands in: an executable,
/// the dialect that decides how a command is passed to it, and the name the model sees in each result.
/// </summary>
/// <param name="Dialect">How the command is passed to the executable.</param>
/// <param name="Executable">A full path, or a file name looked up on <c>PATH</c>. A missing one makes each call fail to start.</param>
/// <param name="Name">The name shown to the model, so it writes commands for this shell. Defaults to the executable's file name.</param>
/// <param name="Launcher">
/// A program and its arguments the shell is started through, such as <c>["bwrap", "--ro-bind", "/", "/", "--"]</c> or
/// <c>["docker", "exec", "-i", "box"]</c>: the process that starts is <c>Launcher[0]</c>, with the rest of the launcher, then
/// <see cref="Executable" />, then the shell's own arguments. This is how a host runs commands in a sandbox it provides; the
/// library names none. Null or empty starts the shell directly.
/// </param>
/// <example><c>new Shell(ShellDialect.Posix, @"C:\Program Files\Git\bin\bash.exe", "bash")</c></example>
public sealed record Shell(ShellDialect Dialect, string Executable, string? Name = null, IReadOnlyList<string>? Launcher = null)
{
    /// <summary>Windows PowerShell 5.1, installed by default on Windows 10 and later. It has no <c>&amp;&amp;</c> or <c>||</c>.</summary>
    public static Shell WindowsPowerShell { get; } = new(ShellDialect.PowerShell, "powershell.exe", "Windows PowerShell 5.1");

    /// <summary>PowerShell 7 (<c>pwsh</c>), a separate install on every OS. It has <c>&amp;&amp;</c> and <c>||</c>.</summary>
    public static Shell PowerShell { get; } = new(ShellDialect.PowerShell, "pwsh", "PowerShell 7");

    /// <summary>The POSIX shell at <c>/bin/sh</c>.</summary>
    public static Shell Sh { get; } = new(ShellDialect.Posix, "/bin/sh", "sh");

    /// <summary><c>bash</c>, looked up on <c>PATH</c>. Models write bash, so <c>[[ ]]</c>, arrays and <c>pipefail</c> work here and not under every <c>sh</c>.</summary>
    public static Shell Bash { get; } = new(ShellDialect.Posix, "bash", "bash");

    private static readonly bool BashIsOnPath = FindOnPath("bash");

    /// <summary>The OS default: <see cref="WindowsPowerShell" /> on Windows, otherwise <see cref="Bash" /> when it is on <c>PATH</c>, else <see cref="Sh" />.</summary>
    public static Shell Default => OperatingSystem.IsWindows() ? WindowsPowerShell : BashIsOnPath ? Bash : Sh;

    /// <summary>The name shown to the model.</summary>
    private static bool FindOnPath(string fileName) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, fileName)));

    /// <summary>How messages and the model's instructions name the shell: <see cref="Name" />, or the executable's file name when there is none.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Path.GetFileNameWithoutExtension(Executable) : Name;
}
