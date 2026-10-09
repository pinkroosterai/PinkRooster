using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn.Shells;

/// <summary>
///     Gives an agent shell command execution on Windows, Linux, and macOS through a host-controlled, bounded interface.
///     Each call runs in a fresh process with closed input, a hard timeout, and output bounded in memory and in length.
/// </summary>
/// <remarks>
///     <para>
///     The prefix lists are a guardrail against honest mistakes, not a sandbox: they match the first words of a command, an
///     allowlist refuses chaining, grouping and redirection, and the process runs with the host's permissions and, unless
///     <c>inheritEnvironment</c> is off, its full environment, including any secrets in it. For a sandbox, start the shell
///     through a <see cref="Shell.Launcher" /> that provides one.
///     </para>
///     <para>
///     <c>RunShell</c> does not ask for approval by default, so an agent runs commands unattended. To have the host approve
///     each command, mark it with the agent builder's <c>RequireApproval("RunShell")</c>: a run that calls it then ends with a
///     <c>ToolApprovalRequestContent</c>.
///     </para>
///     <para>
///     The POSIX path (<see cref="Shell.Sh" />, and every shell on Linux and macOS) is exercised by the tests on Linux, where it is the default.
///     </para>
/// </remarks>
public sealed class ShellToolCollection : ToolCollection
{
    /// <summary>The smallest output budget: room for the status header, labels, and truncation markers.</summary>
    public const int MinOutputCharacters = 200;

    // How long to wait for output after the process ended. A background process the command started can hold the pipes open.
    private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(2);

    // Rejected when an allowlist is set, so an allowed prefix can't carry a second command or write a file by redirection.
    // PowerShell runs (...) in arguments.
    private static readonly char[] ChainingCharacters = [';', '&', '|', '`', '(', ')', '<', '>', '\n', '\r'];

    // A command that only changes directory: it has no lasting effect, because every call starts in the workspace root.
    private static readonly Regex DirectoryChangeOnly = new(@"^\s*(cd|chdir|pushd|set-location|sl)(\s+[^;&|\r\n]*)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Added to the inherited environment so tools fail instead of prompting, and print without colour: git's terminal
    ///     prompt and Git Credential Manager's windows would otherwise wait out the timeout. The host's
    ///     <c>environment</c> argument overrides these.
    /// </summary>
    public static IReadOnlyDictionary<string, string> NonInteractiveEnvironment { get; } = new Dictionary<string, string>
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GCM_INTERACTIVE"] = "never",
        ["NO_COLOR"] = "1"
    };

    private readonly IReadOnlyList<string> allowlistPrefixes;
    private readonly IReadOnlyList<string> denylistPrefixes;
    private readonly Workspace workspace;
    private readonly bool separateStreams;
    private readonly bool inheritEnvironment;
    private readonly IReadOnlyDictionary<string, string?> environment;
    private readonly int maxOutputCharacters;
    private readonly TimeSpan maxTimeout;
    private readonly Shell shell;
    private readonly string startingDirectory;
    private readonly TimeSpan timeout;

    /// <summary>Creates a shell tool collection on the current working directory, with the default limits and no allow or deny list.</summary>
    /// <remarks>For another workspace use the other constructor; for limits, lists, the shell or the environment use <see cref="ShellToolCollectionBuilder" />.</remarks>
    public ShellToolCollection() : this(workspace: null, timeout: null)
    {
    }

    /// <summary>Creates a shell tool collection on <paramref name="workspace" />, with the default limits and no allow or deny list.</summary>
    /// <remarks>For limits, lists, the shell or the environment use <see cref="ShellToolCollectionBuilder" />.</remarks>
    /// <param name="workspace">The folder commands start in and may not leave; share it with the file collections.</param>
    /// <exception cref="ArgumentNullException">The workspace is null.</exception>
    public ShellToolCollection(Workspace workspace) : this(workspace ?? throw new ArgumentNullException(nameof(workspace)), timeout: null)
    {
    }

    /// <summary>
    ///     Creates a shell tool collection with host-configured resource limits and execution policies; <see cref="ShellToolCollectionBuilder" /> is the way in.
    /// </summary>
    /// <param name="workspace">The folder commands start in and may not leave. Defaults to a workspace on the current working directory.</param>
    /// <param name="timeout">
    ///     Execution duration before process tree termination when the model asks for no other. Defaults to 120 seconds.
    /// </param>
    /// <param name="maxOutputCharacters">
    ///     Maximum response length in characters; longer output is middle-truncated per stream. Defaults to 30,000 and must
    ///     be at least <see cref="MinOutputCharacters" />.
    /// </param>
    /// <param name="allowlistPrefixes">
    ///     Optional command prefixes, matched ignoring case and on word boundaries (<c>git</c> allows <c>git log</c>, not <c>gitk</c>;
    ///     <c>git log</c> allows only that), one of which a command must start with. When set, commands containing chaining,
    ///     grouping or redirection characters (<c>; &amp; | ` ( ) &lt; &gt;</c> or a line break) are rejected as well.
    /// </param>
    /// <param name="denylistPrefixes">
    ///     Optional command prefixes, matched like the allowlist, that are refused even when the allowlist allows them: allow
    ///     <c>git</c>, deny <c>git push</c>. Checked first. It needs <paramref name="allowlistPrefixes" />: on its own a denylist
    ///     does not see past chaining, so the constructor refuses it.
    /// </param>
    /// <param name="shell">The shell to run commands in. Defaults to <see cref="Shell.Default" /> for the OS.</param>
    /// <param name="maxTimeout">
    ///     The longest timeout the model may ask for with <c>timeoutSeconds</c>; longer requests are capped. Defaults to
    ///     10 minutes, or <paramref name="timeout" /> when that is longer, and may not be shorter than it.
    /// </param>
    /// <param name="inheritEnvironment">
    ///     <c>false</c> to start commands from an empty environment plus <see cref="NonInteractiveEnvironment" /> and
    ///     <paramref name="environment" />, so the host's secrets do not reach them. Then pass what the commands need, at least <c>PATH</c>
    ///     (and <c>HOME</c> for git, or <c>USERPROFILE</c> on Windows). The shell itself is still found on the host's <c>PATH</c>.
    /// </param>
    /// <param name="separateStreams">
    ///     <c>true</c> to report stdout and stderr in two sections, <c>Stdout:</c> and <c>Stderr:</c>. By default they are merged into one
    ///     <c>Output:</c> section in the order they were written, so an error sits next to the output it belongs to; a merged result
    ///     that is too long for the reply is also saved to a file under the workspace's <c>.pinkrooster/output</c> folder.
    /// </param>
    /// <param name="environment">
    ///     Variables to set for each command on top of the inherited environment and
    ///     <see cref="NonInteractiveEnvironment" />; a null value removes the variable.
    /// </param>
    internal ShellToolCollection(
        Workspace? workspace = null,
        TimeSpan? timeout = null,
        int? maxOutputCharacters = null,
        IReadOnlyList<string>? allowlistPrefixes = null,
        IReadOnlyList<string>? denylistPrefixes = null,
        Shell? shell = null,
        TimeSpan? maxTimeout = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool separateStreams = false,
        bool inheritEnvironment = true)
    {
        this.workspace = workspace ?? new Workspace(Directory.GetCurrentDirectory());
        this.startingDirectory = this.workspace.RootDirectory;
        this.separateStreams = separateStreams;

        this.timeout = timeout ?? TimeSpan.FromSeconds(120);
        if (this.timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }

        this.maxOutputCharacters = maxOutputCharacters ?? 30000;
        if (this.maxOutputCharacters < MinOutputCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputCharacters),
                $"Max output characters must be at least {MinOutputCharacters}.");
        }

        this.maxTimeout = maxTimeout ?? (this.timeout > TimeSpan.FromMinutes(10) ? this.timeout : TimeSpan.FromMinutes(10));
        if (this.maxTimeout < this.timeout)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTimeout), "Max timeout must not be shorter than the timeout.");
        }

        this.allowlistPrefixes = allowlistPrefixes ?? [];
        this.denylistPrefixes = denylistPrefixes ?? [];
        if (this.denylistPrefixes.Count > 0 && this.allowlistPrefixes.Count == 0)
        {
            throw new ArgumentException(
                "denylistPrefixes needs allowlistPrefixes: on its own a denylist does not see past chaining (a ; or && after an allowed word). " +
                "Add an allowlist, and the denylist carves exceptions out of it, or drop the denylist.", nameof(denylistPrefixes));
        }

        this.inheritEnvironment = inheritEnvironment;
        this.shell = shell ?? Shell.Default;
        this.environment = environment ?? new Dictionary<string, string?>();
        foreach (string name in this.environment.Keys)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('='))
            {
                throw new ArgumentException($"Environment variable name '{name}' must not be blank or contain '='.", nameof(environment));
            }
        }

        // A launcher changes how the shell starts, not which shell it is
        Shell bareShell = this.shell with { Launcher = null };
        AddInstruction(bareShell == Shell.WindowsPowerShell
            ? "Commands run in Windows PowerShell 5.1: chain them with ; or if ($?) { ... }, because && and || don't exist there."
            : this.shell.Dialect == ShellDialect.PowerShell
                ? $"Commands run in {this.shell.DisplayName}; write PowerShell syntax."
                : bareShell == Shell.Bash
                    ? "Commands run in bash; write bash syntax."
                    : $"Commands run in {this.shell.DisplayName}, a POSIX shell; write sh syntax.");
        AddInstruction($"Commands start in the base directory {this.startingDirectory}; workingDirectory must stay inside it.");
        AddInstruction($"Commands are stopped after {Seconds(this.timeout)} unless timeoutSeconds asks for more (up to {Seconds(this.maxTimeout)}). " +
                       $"A reply is limited to {this.maxOutputCharacters} characters.");
        if (this.denylistPrefixes.Count > 0)
        {
            AddConstraint($"Commands starting with any of these are refused: {Quote(this.denylistPrefixes)}.");
        }

        if (this.allowlistPrefixes.Count > 0)
        {
            AddConstraint($"Only commands starting with one of these run: {Quote(this.allowlistPrefixes)}. Run one command per call; " +
                          "chaining, grouping and redirection characters are refused.");
        }
    }

    private static string Quote(IEnumerable<string> prefixes) => string.Join(", ", prefixes.Select(prefix => $"'{prefix}'"));

    /// <summary>The <c>RunShell</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("RunShell",
        "Runs one command in a fresh, non-interactive shell and returns its status ('Exited' with an exit code, 'TimedOut' or 'StartFailed') and its output. " +
        "Use it for builds, tests, git and other programs; when you have the file tools (ReadFile, SearchFiles, FindFiles, EditFile), use them to read, search and change files. " +
        "Nothing carries over between calls, so pass workingDirectory instead of using cd. Input is closed, so a command that waits for input or credentials fails. " +
        "Long output is shortened in the middle, and when the result names a file the whole output is saved there; use outputFilters to keep only the lines you need. " +
        "The command runs with the host's permissions. A command the host's policy refuses returns an error saying why, without running.",
        Kind = ToolKind.Execute)]
    public Task<string> RunShell(
        [Description("The command to run, in the syntax of the shell the instructions name.")]
        string command,
        [Description("Optional directory to run in, relative to the base directory. Leave it out to run in the base directory itself.")]
        string? workingDirectory = null,
        [Description("Optional keywords; only output lines containing at least one of them (ignoring case) are kept.")]
        IReadOnlyList<string>? outputFilters = null,
        [Description("Optional seconds before the command is stopped; raise it for a slow build or test run. The instructions give the default and the maximum.")]
        int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunAsync(() => ExecuteShellCommandAsync(command, workingDirectory, outputFilters, timeoutSeconds, cancellationToken));
    }

    private async Task<string> ExecuteShellCommandAsync(
        string command,
        string? workingDirectory,
        IReadOnlyList<string>? outputFilters,
        int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "Error: Command cannot be empty or whitespace.";
        }

        if (timeoutSeconds is < 1)
        {
            return "Error: timeoutSeconds must be at least 1.";
        }

        List<string> notes = new List<string>();
        TimeSpan effectiveTimeout = timeout;
        if (timeoutSeconds is { } requested)
        {
            effectiveTimeout = TimeSpan.FromSeconds(requested);
            if (effectiveTimeout > maxTimeout)
            {
                effectiveTimeout = maxTimeout;
                notes.Add($"Note: timeoutSeconds {requested} was capped at the host maximum of {Seconds(maxTimeout)}.");
            }
        }

        if (DirectoryChangeOnly.IsMatch(command))
        {
            notes.Add("Note: a command that only changes directory has no lasting effect; every call starts in the workspace root. Pass workingDirectory instead.");
        }

        // 1. Policy enforcement
        string? policyError = CheckPolicy(command.TrimStart());
        if (policyError is not null)
        {
            return policyError;
        }

        // 2. Resolve and validate working directory bounds
        string effectiveWorkingDir = startingDirectory;
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            // Symbolic links are resolved, so a linked subdirectory can't lead out of the starting directory
            if (!PathGuard.TryResolvePath(startingDirectory, workingDirectory, out string? combined, out _))
            {
                return "Error: workingDirectory must stay inside the base directory; pass a relative path such as 'src'.";
            }

            if (!Directory.Exists(combined))
            {
                return $"Error: Working directory does not exist: '{workingDirectory}'";
            }

            effectiveWorkingDir = combined;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Process start, with input closed so a command that reads it gets end-of-file instead of the host's console
        using Process process = new Process { StartInfo = CreateProcessStartInfo(command, effectiveWorkingDir) };
        try
        {
            if (!process.Start())
            {
                return $"Status: StartFailed\nShell: {shell.DisplayName}\nError: Failed to start shell process.";
            }
        }
        catch (Exception ex)
        {
            return $"Status: StartFailed\nShell: {shell.DisplayName}\nError: {ex.Message}";
        }

        process.StandardInput.Close();

        // 4. Concurrent, bounded pipe draining
        string[] filters = outputFilters?.Where(filter => !string.IsNullOrWhiteSpace(filter)).ToArray() ?? [];
        // Merged, both streams feed one capture, which keeps their lines in arrival order and saves a long output to a file;
        // the second capture then stays empty.
        OutputSpill? spill = separateStreams ? null : new OutputSpill(workspace);
        ShellOutputCapture stdout = new ShellOutputCapture(filters, maxOutputCharacters, spill);
        ShellOutputCapture stderr = separateStreams ? new ShellOutputCapture(filters, maxOutputCharacters) : stdout;
        Task readers = Task.WhenAll(
            stdout.ReadAsync(process.StandardOutput, CancellationToken.None),
            stderr.ReadAsync(process.StandardError, CancellationToken.None));

        // 5. Wait for exit, the timeout, or the caller's cancellation
        List<string> warnings = new List<string>();
        string executionState;
        int? exitCode = null;

        using CancellationTokenSource timeoutSource = new CancellationTokenSource(effectiveTimeout);
        using CancellationTokenSource waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
            executionState = "Exited";
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException) when (waitSource.IsCancellationRequested)
        {
            KillProcessTree(process, warnings);
            cancellationToken.ThrowIfCancellationRequested();
            executionState = "TimedOut";
            notes.Add(effectiveTimeout < maxTimeout
                ? $"Timeout: {Seconds(effectiveTimeout)}; pass timeoutSeconds for up to {Seconds(maxTimeout)}."
                : $"Timeout: {Seconds(effectiveTimeout)}, the host maximum.");
        }

        if (!await DrainAsync(readers).ConfigureAwait(false))
        {
            warnings.Add("Warning: output streams stayed open after the shell process ended; a background process it " +
                         "started may still be running, and later output was not captured.");
        }

        if (separateStreams)
        {
            foreach ((string? name, ShellOutputCapture? capture) in new[] { ("stdout", stdout), ("stderr", stderr) })
            {
                if (capture.ReadError is not null)
                {
                    warnings.Add($"Warning: reading {name} stopped early: {capture.ReadError}");
                }
            }
        }
        else if (stdout.ReadError is not null)
        {
            warnings.Add($"Warning: reading the output stopped early: {stdout.ReadError}");
        }

        if (spill is not null)
        {
            AddSpillNote(spill, stdout, executionState, exitCode, notes, warnings, filters.Length > 0);
        }

        // 6. Format within the output budget
        return FormatResult(executionState, exitCode, [.. notes, .. warnings], stdout, separateStreams ? stderr : null, filters.Length > 0);
    }

    // Keeps the saved file and names it when the output does not fit the reply; otherwise the file is not needed and goes.
    private void AddSpillNote(
        OutputSpill spill,
        ShellOutputCapture output,
        string executionState,
        int? exitCode,
        List<string> notes,
        List<string> warnings,
        bool filtered)
    {
        // The room FormatResult leaves for the output: the budget less the header lines and the section label.
        long header = $"Status: {executionState}".Length
                      + (exitCode.HasValue ? $"\nExit Code: {exitCode.Value}".Length : 0)
                      + notes.Concat(warnings).Sum(line => line.Length + 1);
        long label = SectionLabel("Output", output, filtered) is { } text ? text.Length + 2 : 0;
        bool cut = output.Length > maxOutputCharacters - header - label;
        spill.Close(keep: cut);

        if (spill.Error is not null)
        {
            warnings.Add($"Warning: could not save the full output: {spill.Error}");
        }
        else if (cut && spill.RelativePath is not null)
        {
            notes.Add($"Output: {output.Length} characters{(filtered ? " kept by the filter" : string.Empty)}; the reply is shortened, " +
                      $"and all of it is saved in {spill.RelativePath} (read it with ReadFile, or search it with SearchFiles).");
        }
    }

    private static string Seconds(TimeSpan duration) => $"{duration.TotalSeconds:0.#} s";

    private string? CheckPolicy(string command)
    {
        string? denied = denylistPrefixes.FirstOrDefault(prefix => StartsWithWords(command, prefix));
        if (denied is not null)
        {
            return $"Error: Command violates host execution policy: '{denied}' is denied.";
        }

        if (allowlistPrefixes.Count == 0)
        {
            return null;
        }

        if (!allowlistPrefixes.Any(prefix => StartsWithWords(command, prefix)))
        {
            return $"Error: Command violates host execution policy: it must start with one of: {Quote(allowlistPrefixes)}.";
        }

        int chained = command.IndexOfAny(ChainingCharacters);
        if (chained >= 0)
        {
            string character = command[chained] is '\n' or '\r' ? "a line break" : $"'{command[chained]}'";
            return $"Error: Command violates host execution policy: {character} is not allowed when an allowlist is set; " +
                   "run one command per call.";
        }

        return null;
    }

    // Whether the command starts with the prefix as whole words, ignoring case: "git" matches "git log" and "git", not "gitk".
    private static bool StartsWithWords(string command, string prefix)
    {
        string words = prefix.Trim();
        return words.Length > 0
               && command.StartsWith(words, StringComparison.OrdinalIgnoreCase)
               && (command.Length == words.Length || char.IsWhiteSpace(command[words.Length]));
    }

    private ProcessStartInfo CreateProcessStartInfo(string command, string workingDirectory)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!inheritEnvironment)
        {
            psi.Environment.Clear();
        }

        foreach ((string? name, string? value) in NonInteractiveEnvironment)
        {
            psi.Environment[name] = value;
        }

        foreach ((string? name, string? value) in environment)
        {
            if (value is null)
            {
                psi.Environment.Remove(name);
            }
            else
            {
                psi.Environment[name] = value;
            }
        }

        // Through a launcher, the launcher is the process and the shell is one of its arguments
        if (shell.Launcher is { Count: > 0 } launcher)
        {
            psi.FileName = launcher[0];
            foreach (string? argument in launcher.Skip(1))
            {
                psi.ArgumentList.Add(argument);
            }

            psi.ArgumentList.Add(shell.Executable);
        }
        else
        {
            psi.FileName = shell.Executable;
        }

        if (shell.Dialect == ShellDialect.PowerShell)
        {
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(WrapPowerShellCommand(command))));
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        return psi;
    }

    /// <summary>
    ///     Wraps a command for Windows PowerShell 5.1 and PowerShell 7: no progress records (they reach redirected stderr as CLIXML), UTF-8
    ///     output, and an exit code that is the last native program's, else 1 when the last statement failed, else 0.
    ///     The command sits on its own lines, so a trailing comment in it can't swallow the exit-code lines.
    /// </summary>
    private static string WrapPowerShellCommand(string command) =>
        "$ProgressPreference = 'SilentlyContinue'\n" +
        "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false\n" +
        "$global:LASTEXITCODE = 0\n" +
        command + "\n" +
        "$pinkRoosterSucceeded = $?\n" +
        "if ($LASTEXITCODE) { exit $LASTEXITCODE }\n" +
        "if (-not $pinkRoosterSucceeded) { exit 1 }\n" +
        "exit 0\n";

    private static void KillProcessTree(Process process, List<string> warnings)
    {
        try
        {
            process.Kill(true);
        }
        catch (InvalidOperationException)
        {
            // The process exited on its own in the meantime, so there is nothing left to stop.
        }
        catch (Exception error)
        {
            warnings.Add($"Warning: could not stop the process tree: {error.Message}");
        }
    }

    private static async Task<bool> DrainAsync(Task readers)
    {
        try
        {
            await readers.WaitAsync(DrainLimit).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private string FormatResult(
        string executionState,
        int? exitCode,
        List<string> notesAndWarnings,
        ShellOutputCapture stdout,
        ShellOutputCapture? stderr,
        bool filtered)
    {
        StringBuilder header = new StringBuilder($"Status: {executionState}");
        if (exitCode.HasValue)
        {
            header.Append($"\nExit Code: {exitCode.Value}");
        }

        foreach (string line in notesAndWarnings)
        {
            header.Append('\n').Append(line);
        }

        string? stdoutLabel = SectionLabel(stderr is null ? "Output" : "Stdout", stdout, filtered);
        string? stderrLabel = stderr is null ? null : SectionLabel("Stderr", stderr, filtered);

        // Split what the header and labels leave between the streams; a stream that needs less hands the rest to the other.
        int available = maxOutputCharacters - header.Length
                                            - (stdoutLabel is null ? 0 : stdoutLabel.Length + 2)
                                            - (stderrLabel is null ? 0 : stderrLabel.Length + 2);
        long stdoutNeed = stdoutLabel is null ? 0 : stdout.Length;
        long stderrNeed = stderrLabel is null ? 0 : stderr!.Length;
        int stdoutBudget, stderrBudget;
        if (stdoutNeed + stderrNeed <= available)
        {
            stdoutBudget = (int)stdoutNeed;
            stderrBudget = (int)stderrNeed;
        }
        else if (stdoutNeed <= available / 2)
        {
            stdoutBudget = (int)stdoutNeed;
            stderrBudget = available - stdoutBudget;
        }
        else if (stderrNeed <= available / 2)
        {
            stderrBudget = (int)stderrNeed;
            stdoutBudget = available - stderrBudget;
        }
        else
        {
            stdoutBudget = available / 2;
            stderrBudget = available - stdoutBudget;
        }

        StringBuilder sb = new StringBuilder(header.ToString());
        AppendSection(sb, stdoutLabel, stdout, stdoutBudget);
        if (stderr is not null)
        {
            AppendSection(sb, stderrLabel, stderr, stderrBudget);
        }

        string response = sb.ToString().TrimEnd();

        // Many notes and warnings can leave the streams no room; the whole response still keeps to the budget.
        return OutputBudget.MiddleTruncate(response, response, response.Length, maxOutputCharacters);
    }

    private static string? SectionLabel(string name, ShellOutputCapture capture, bool filtered)
    {
        if (filtered)
        {
            return capture.TotalLines == 0
                ? null
                : $"{name} (filter kept {capture.MatchedLines} of {capture.TotalLines} lines):";
        }

        return capture.HasText ? $"{name}:" : null;
    }

    private static void AppendSection(StringBuilder sb, string? label, ShellOutputCapture capture, int budget)
    {
        if (label is null)
        {
            return;
        }

        sb.Append('\n').Append(label);
        string text = capture.Render(Math.Max(budget, 0));
        if (text.Length > 0)
        {
            sb.Append('\n').Append(text);
        }
    }
}
