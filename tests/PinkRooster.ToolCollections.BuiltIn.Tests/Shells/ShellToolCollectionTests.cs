using System.Diagnostics;
using PinkRooster.ToolCollections.BuiltIn.Shared;
using PinkRooster.ToolCollections.BuiltIn.Shells;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.Shells;

/// <summary>
/// Runs real shell processes: Windows PowerShell 5.1 on Windows, /bin/sh elsewhere. Each command is given in both dialects;
/// tests of a PowerShell-only rule, or of a shell this machine lacks, return early.
/// </summary>
public sealed class ShellToolCollectionTests : IDisposable
{
    private static readonly bool IsWindows = OperatingSystem.IsWindows();

    private readonly TempDirectory temp = new("shell-tools-");

    private string root => temp.Path;

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task RunShell_SuccessfulCommand_ReturnsExitedStatusCodeShellAndStdout()
    {
        string result = await Collection().RunShell(Echo("hello"));

        Assert.Equal($"Status: Exited\nExit Code: 0\nOutput:\nhello", result);
    }

    [Fact]
    public async Task RunShell_DeniedPrefix_IsRefusedWithoutStartingAProcess()
    {
        ShellToolCollection collection = Collection(allowlist: ["New-Item", "touch", "echo"], denylist: ["New-Item", "touch"]);

        string result = await collection.RunShell(CreateMarker());

        Assert.StartsWith("Error: Command violates host execution policy: '", result);
        Assert.Contains("is denied", result);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void Constructor_DenylistWithoutAllowlist_ThrowsNamingTheFix()
    {
        var error = Assert.Throws<ArgumentException>(() => Collection(denylist: ["rm"]));

        Assert.Contains("denylistPrefixes needs allowlistPrefixes", error.Message);
        Assert.Contains("Add an allowlist", error.Message);
    }

    [Fact]
    public async Task RunShell_DenylistCarvesAnExceptionOutOfTheAllowlist()
    {
        ShellToolCollection collection = Collection(allowlist: ["echo"], denylist: ["echo secret"]);

        Assert.Equal("Error: Command violates host execution policy: 'echo secret' is denied.", await collection.RunShell("echo secret"));
        Assert.Equal("Status: Exited\nExit Code: 0\nOutput:\nfine", await collection.RunShell("echo fine"));
    }

    [Theory]
    [InlineData("echo")]
    [InlineData("echo ")]
    [InlineData("ECHO")]
    public async Task RunShell_AllowlistEntry_MatchesWholeWordsIgnoringCaseAndATrailingSpace(string entry)
    {
        ShellToolCollection collection = Collection(allowlist: [entry]);

        Assert.StartsWith("Status: Exited", await collection.RunShell("echo hi"));
        Assert.StartsWith("Error: Command violates host execution policy: it must start with one of:", await collection.RunShell("echoes hi"));
    }

    [Fact]
    public async Task RunShell_AllowedWordThatOnlyStartsAnotherWord_IsRefused()
    {
        ShellToolCollection collection = Collection(allowlist: ["git"]);

        string result = await collection.RunShell("gitk --all");

        Assert.Equal("Error: Command violates host execution policy: it must start with one of: 'git'.", result);
    }

    [Fact]
    public async Task RunShell_MultiWordAllowlistEntry_AllowsOnlyThatSubcommand()
    {
        ShellToolCollection collection = Collection(allowlist: ["git status"]);

        Assert.StartsWith("Error: Command violates host execution policy: it must start with one of:", await collection.RunShell("git statusx"));
        Assert.StartsWith("Error: Command violates host execution policy: it must start with one of:", await collection.RunShell("git push"));
    }

    [Theory]
    [InlineData("echo hi > marker.txt", "'>'")]
    [InlineData("echo hi 2> marker.txt", "'>'")]
    [InlineData("cat < marker.txt", "'<'")]
    public async Task RunShell_RedirectionAfterAnAllowedPrefix_IsRefused(string command, string named)
    {
        ShellToolCollection collection = Collection(allowlist: ["echo", "cat"]);

        string result = await collection.RunShell(command);

        Assert.Equal($"Error: Command violates host execution policy: {named} is not allowed when an allowlist is set; run one command per call.", result);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public async Task RunShell_RedirectionWithoutAnAllowlist_IsPassedToTheShell()
    {
        await Collection().RunShell("echo hi > marker.txt");

        Assert.True(File.Exists(MarkerPath));
    }

    [Fact]
    public async Task RunShell_CommandOutsideAllowlist_IsRefusedAndNamesTheAllowedPrefixes()
    {
        ShellToolCollection collection = Collection(allowlist: ["git ", "dotnet "]);

        string result = await collection.RunShell(CreateMarker());

        Assert.Equal("Error: Command violates host execution policy: it must start with one of: 'git ', 'dotnet '.", result);
        Assert.False(File.Exists(MarkerPath));
    }

    [Theory]
    [InlineData("; ", "';'")]
    [InlineData(" && ", "'&'")]
    [InlineData(" | ", "'|'")]
    [InlineData(" $(", "'('")]
    [InlineData("\n", "a line break")]
    public async Task RunShell_ChainingAfterAnAllowedPrefix_IsRefused(string separator, string named)
    {
        ShellToolCollection collection = Collection(allowlist: ["git "]);

        string result = await collection.RunShell($"git --version{separator}{CreateMarker()}");

        Assert.Equal($"Error: Command violates host execution policy: {named} is not allowed when an allowlist is set; run one command per call.", result);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public async Task RunShell_WorkingDirectoryOutsideTheBaseDirectory_IsRefused()
    {
        string result = await Collection().RunShell(Echo("x"), workingDirectory: "..");

        Assert.Equal($"Error: workingDirectory must stay inside the base directory; pass a relative path such as 'src'.", result);
    }

    [Fact]
    public async Task RunShell_WorkingDirectoryLinkedOutsideTheBaseDirectory_IsRefused()
    {
        using var outside = new TempDirectory("shell-outside-");
        SymbolicLinks.ToDirectory(Path.Combine(root, "exit"), outside.Path);

        string result = await Collection().RunShell(Echo("x"), workingDirectory: "exit");

        Assert.StartsWith("Error: workingDirectory must stay inside the base directory", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A=B")]
    public void Constructor_EnvironmentNameThatCannotBeSet_Throws(string name)
    {
        var environment = new Dictionary<string, string?> { [name] = "x" };

        var error = Assert.Throws<ArgumentException>(() => Collection(environment: environment));

        Assert.Contains("Environment variable name", error.Message);
    }

    [Fact]
    public void Defaults_AreTunedForBuildsAndTestRuns()
    {
        var collection = new ShellToolCollection(new Workspace(root));

        Assert.Contains(collection.Instructions, i => i.Contains("stopped after 120 s") && i.Contains("30000 characters"));
    }

    [Fact]
    public void DefaultShell_OnPosix_IsBashWhenItIsOnPath()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows defaults to PowerShell.");
        bool bashOnPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, "bash")));

        Assert.Equal(bashOnPath ? Shell.Bash : Shell.Sh, Shell.Default);
        if (bashOnPath)
        {
            Assert.Contains("Commands run in bash; write bash syntax.", new ShellToolCollection(new Workspace(root)).Instructions);
        }
    }

    [Fact]
    public async Task RunShell_OnPosixDefault_UnderstandsBashSyntax()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Shell.Default != Shell.Bash, "Needs bash as the default shell.");

        string result = await Collection(shell: Shell.Bash).RunShell("[[ abc == a* ]] && echo matched");

        Assert.Equal("Status: Exited\nExit Code: 0\nOutput:\nmatched", result);
    }

    [Theory]
    [InlineData("cd src")]
    [InlineData("  cd ..  ")]
    [InlineData("cd")]
    public async Task RunShell_CommandThatOnlyChangesDirectory_SaysItHasNoLastingEffect(string command)
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Assert.SkipWhen(IsWindows, "The commands are POSIX shell syntax.");

        string result = await Collection(shell: Shell.Sh).RunShell(command);

        Assert.Contains("Note: a command that only changes directory has no lasting effect; every call starts in the workspace root. Pass workingDirectory instead.", result);
    }

    [Fact]
    public async Task RunShell_CommandThatChangesDirectoryAndDoesMore_HasNoNote()
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));

        string result = await Collection().RunShell(IsWindows ? "cd src; Write-Output 'x'" : "cd src && echo x");

        Assert.DoesNotContain("no lasting effect", result);
    }

    [Fact]
    public async Task RunShell_Launcher_StartsTheShellThroughIt()
    {
        Assert.SkipWhen(IsWindows, "The stub launcher is a POSIX script.");
        string launcher = Path.Combine(root, "launcher.sh");
        File.WriteAllText(launcher, $"#!/bin/sh\necho \"$@\" > '{Path.Combine(root, "launched.txt")}'\nshift\nexec \"$@\"\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Shell throughLauncher = Shell.Sh with { Launcher = [launcher, "--flag"] };

        string result = await Collection(shell: throughLauncher).RunShell("echo from-launcher");

        Assert.Equal("Status: Exited\nExit Code: 0\nOutput:\nfrom-launcher", result);
        Assert.Equal("--flag /bin/sh -c echo from-launcher", File.ReadAllText(Path.Combine(root, "launched.txt")).Trim());
    }

    [Fact]
    public void Instructions_NameTheShellEvenWhenItStartsThroughALauncher()
    {
        ShellToolCollection collection = Collection(shell: Shell.Bash with { Launcher = ["sandbox-run"] });

        Assert.Contains("Commands run in bash; write bash syntax.", collection.Instructions);
    }

    [Fact]
    public async Task RunShell_WithoutInheritingTheEnvironment_ReachesTheCommandWithOnlyWhatTheHostNamed()
    {
        Assert.SkipWhen(IsWindows, "PowerShell needs its own variables to start.");

        var host = new Dictionary<string, string?> { ["PINK_TEST"] = "named" };
        string clean = await Collection(shell: Shell.Sh, environment: host, inheritEnvironment: false).RunShell("echo \"[$HOME][$PINK_TEST][$NO_COLOR]\"");
        string inherited = await Collection(shell: Shell.Sh, environment: host).RunShell("echo \"[$HOME][$PINK_TEST][$NO_COLOR]\"");

        Assert.Equal("Status: Exited\nExit Code: 0\nOutput:\n[][named][1]", clean);
        Assert.DoesNotContain("[][named]", inherited);
        Assert.Contains("[named][1]", inherited);
    }

    [Fact]
    public async Task RunShell_MissingWorkingDirectory_IsRefused()
    {
        string result = await Collection().RunShell(Echo("x"), workingDirectory: "missing");

        Assert.Equal("Error: Working directory does not exist: 'missing'", result);
    }

    [Fact]
    public async Task RunShell_WorkingDirectory_RunsInThatSubdirectory()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        string result = await Collection().RunShell(IsWindows ? "(Get-Location).Path" : "pwd -P", workingDirectory: "sub");

        Assert.EndsWith(Path.DirectorySeparatorChar + "sub", result);
        Assert.Contains(Path.GetFileName(root), result);
    }

    [Fact]
    public async Task RunShell_ExitStatement_ReturnsItsExitCode()
    {
        string result = await Collection().RunShell("exit 3");

        Assert.Equal($"Status: Exited\nExit Code: 3", result);
    }

    [Fact]
    public async Task RunShell_NativeProgramExitCode_IsPassedThrough()
    {
        string result = await Collection().RunShell(IsWindows ? "cmd /c exit 5" : "sh -c 'exit 5'");

        Assert.Equal($"Status: Exited\nExit Code: 5", result);
    }

    [Fact]
    public async Task RunShell_FailingCmdletAsLastStatement_ExitsWithOne()
    {
        if (!IsWindows)
        {
            return; // The 0/1 rule for cmdlets is PowerShell's; sh reports the program's own code.
        }

        string result = await Collection().RunShell("Get-Item 'does-not-exist-here'");

        Assert.StartsWith($"Status: Exited\nExit Code: 1\nOutput:", result);
    }

    [Fact]
    public async Task RunShell_EmbeddedQuotes_ReachTheShellUnchanged()
    {
        string result = await Collection().RunShell(IsWindows
            ? "Write-Output \"say \"\"hi\"\" and 'bye'\""
            : "echo \"say \\\"hi\\\" and 'bye'\"");

        Assert.EndsWith("Output:\nsay \"hi\" and 'bye'", result);
    }

    [Fact]
    public async Task RunShell_NonAsciiOutput_IsReadAsUtf8()
    {
        string result = await Collection().RunShell(Echo("héllo ✓ 日本"));

        Assert.EndsWith("Output:\nhéllo ✓ 日本", result);
    }

    [Fact]
    public async Task RunShell_TerminalEscapeSequences_AreRemoved()
    {
        string result = await Collection().RunShell(IsWindows
            ? "Write-Output ([char]27 + '[1;31mred' + [char]27 + '[0m plain'); Write-Output ([char]27 + ']0;title' + [char]7 + 'text')"
            : "printf '\\033[1;31mred\\033[0m plain\\n\\033]0;title\\007text\\n'");

        Assert.EndsWith("Output:\nred plain\ntext", result);
    }

    [Fact]
    public async Task RunShell_SeparateStreams_ReportsStdoutAndStderrInTheirOwnSections()
    {
        string result = await Collection(separateStreams: true).RunShell(IsWindows
            ? "[Console]::Error.WriteLine('oops'); Write-Output 'fine'"
            : "echo oops >&2; echo fine");

        Assert.Equal($"Status: Exited\nExit Code: 0\nStdout:\nfine\nStderr:\noops", result);
    }

    [Fact]
    public async Task RunShell_MergedStreams_KeepStdoutAndStderrInTheOrderTheyWereWritten()
    {
        string result = await Collection().RunShell(IsWindows
            ? "Write-Output 'one'; Start-Sleep -Milliseconds 400; [Console]::Error.WriteLine('two'); Start-Sleep -Milliseconds 400; Write-Output 'three'"
            : "echo one; sleep 0.4; echo two >&2; sleep 0.4; echo three");

        Assert.Equal("Status: Exited\nExit Code: 0\nOutput:\none\ntwo\nthree", result);
    }

    [Fact]
    public async Task RunShell_CommandThatReadsInput_GetsEndOfFileAndFinishes()
    {
        Stopwatch watch = Stopwatch.StartNew();

        string result = await Collection(timeout: TimeSpan.FromSeconds(20)).RunShell(IsWindows
            ? "$null = [Console]::In.ReadToEnd(); Write-Output 'done'"
            : "cat; echo done");

        Assert.EndsWith("Output:\ndone", result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    [Fact]
    public async Task RunShell_OutputFilters_KeepMatchingLinesOfBothStreamsIgnoringCaseAndCountThem()
    {
        string result = await Collection(separateStreams: true).RunShell(FilterSample(), outputFilters: ["alpha"]);

        Assert.Equal(
            $"Status: Exited\nExit Code: 0\n" +
            "Stdout (filter kept 2 of 3 lines):\nAlpha one\nALPHA three\n" +
            "Stderr (filter kept 1 of 2 lines):\nalpha err",
            result);
    }

    [Fact]
    public async Task RunShell_OutputFiltersOnMergedStreams_KeepMatchingLinesOfBothAndCountThem()
    {
        string result = await Collection().RunShell(FilterSample(), outputFilters: ["alpha"]);

        Assert.StartsWith("Status: Exited\nExit Code: 0\nOutput (filter kept 3 of 5 lines):\n", result);
        Assert.Contains("Alpha one", result);
        Assert.Contains("ALPHA three", result);
        Assert.Contains("alpha err", result);
        Assert.DoesNotContain("beta two", result);
    }

    [Fact]
    public async Task RunShell_OutputFiltersWithoutMatches_StillSayHowManyLinesWereDropped()
    {
        string result = await Collection(separateStreams: true).RunShell(FilterSample(), outputFilters: ["zzz"]);

        Assert.Equal(
            $"Status: Exited\nExit Code: 0\n" +
            "Stdout (filter kept 0 of 3 lines):\n" +
            "Stderr (filter kept 0 of 2 lines):",
            result);
    }

    [Theory]
    [InlineData(38)]
    [InlineData(40)]
    [InlineData(42)]
    [InlineData(44)]
    [InlineData(46)]
    [InlineData(48)]
    [InlineData(50)]
    [InlineData(53)]
    [InlineData(56)]
    [InlineData(60)]
    [InlineData(100)]
    [InlineData(5000)]
    public async Task RunShell_OutputOverBudget_IsMiddleTruncatedWithinTheBudget(int lines)
    {
        const int budget = 500;
        int roomForStdout = budget - $"Status: Exited\nExit Code: 0".Length - "\nOutput:\n".Length;

        string result = await Collection(maxOutputCharacters: budget).RunShell(NumberedLines(lines));

        Assert.True(result.Length <= budget, $"{result.Length} characters for a budget of {budget}.");
        Assert.Contains("Output:\nline00001", result);
        Assert.EndsWith($"line{lines:D5}", result);
        Assert.Equal(lines * 10 > roomForStdout, result.Contains(" characters omitted ...]"));
    }

    [Fact]
    public async Task RunShell_FiveMegabytesOfOutput_StaysWithinTheBudget()
    {
        string line = new('x', 99);
        string result = await Collection(maxOutputCharacters: 1000).RunShell(IsWindows
            ? $"[Console]::Out.Write(('{line}' + \"`n\") * 50000)"
            : $"yes '{line}' | head -n 50000");

        Assert.True(result.Length <= 1000, $"{result.Length} characters.");
        Assert.Contains(" characters omitted ...]", result);
    }

    [Fact]
    public async Task RunShell_LargeStdoutAndStderr_KeepBothLabels()
    {
        string result = await Collection(maxOutputCharacters: 600, separateStreams: true).RunShell(IsWindows
            ? "1..200 | ForEach-Object { Write-Output ('out{0:D5}' -f $_); [Console]::Error.WriteLine(('err{0:D5}' -f $_)) }"
            : "i=1; while [ $i -le 200 ]; do printf 'out%05d\\n' $i; printf 'err%05d\\n' $i >&2; i=$((i+1)); done");

        Assert.True(result.Length <= 600, $"{result.Length} characters.");
        Assert.Contains("Stdout:\nout00001", result);
        Assert.Contains("out00200\nStderr:\nerr00001", result);
        Assert.EndsWith("err00200", result);
    }

    [Fact]
    public async Task RunShell_Timeout_KillsTheProcessKeepsEarlierOutputAndSaysHowToAskForMore()
    {
        Stopwatch watch = Stopwatch.StartNew();

        string result = await Collection(timeout: TimeSpan.FromSeconds(3)).RunShell(Sleep("before", 15));

        Assert.Equal($"Status: TimedOut\nTimeout: 3 s; pass timeoutSeconds for up to 600 s.\nOutput:\nbefore", result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    [Fact]
    public async Task RunShell_RequestedTimeout_ReplacesTheDefault()
    {
        Stopwatch watch = Stopwatch.StartNew();

        string result = await Collection(timeout: TimeSpan.FromSeconds(30)).RunShell(Sleep("before", 15), timeoutSeconds: 3);

        Assert.StartsWith($"Status: TimedOut\nTimeout: 3 s; pass timeoutSeconds for up to 600 s.", result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    [Fact]
    public async Task RunShell_RequestedTimeoutOverTheMaximum_IsCappedAndSaysSo()
    {
        Stopwatch watch = Stopwatch.StartNew();

        string result = await Collection(timeout: TimeSpan.FromSeconds(2), maxTimeout: TimeSpan.FromSeconds(3))
            .RunShell(Sleep("before", 15), timeoutSeconds: 100);

        Assert.Equal(
            $"Status: TimedOut\n" +
            "Note: timeoutSeconds 100 was capped at the host maximum of 3 s.\n" +
            "Timeout: 3 s, the host maximum.\nOutput:\nbefore",
            result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    [Fact]
    public async Task RunShell_RequestedTimeoutBelowOneSecond_IsRefused()
    {
        string result = await Collection().RunShell(Echo("x"), timeoutSeconds: 0);

        Assert.Equal("Error: timeoutSeconds must be at least 1.", result);
    }

    [Fact]
    public async Task RunShell_AlreadyCancelledToken_ThrowsWithoutStartingAProcess()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Collection().RunShell(CreateMarker(), cancellationToken: source.Token));
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public async Task RunShell_CancelledWhileRunning_StopsTheProcessAndThrows()
    {
        using CancellationTokenSource source = new(TimeSpan.FromSeconds(2));
        Stopwatch watch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Collection().RunShell(IsWindows ? "Start-Sleep -Seconds 15" : "sleep 15", cancellationToken: source.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    [Fact]
    public async Task RunShell_BackgroundProcessHoldingTheOutput_ReturnsAfterTheDrainLimitWithAWarning()
    {
        Stopwatch watch = Stopwatch.StartNew();

        // The leftover child runs from the drive root so it doesn't lock the test directory against deletion.
        string result = await Collection().RunShell(IsWindows
            ? "cmd /c \"start /b /d \\ powershell -NoProfile -Command Start-Sleep 10\"; Write-Output 'started'"
            : "(cd / && sleep 10) & echo started");

        Assert.StartsWith($"Status: Exited\nExit Code: 0\nWarning: output streams stayed open", result);
        Assert.EndsWith("Output:\nstarted", result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"Took {watch.Elapsed}.");
    }

    [Fact]
    public async Task RunShell_NonInteractiveVariables_AreSetForTheCommand()
    {
        string result = await Collection().RunShell(IsWindows
            ? "Write-Output \"$env:GIT_TERMINAL_PROMPT $env:GCM_INTERACTIVE $env:NO_COLOR\""
            : "echo \"$GIT_TERMINAL_PROMPT $GCM_INTERACTIVE $NO_COLOR\"");

        Assert.EndsWith("Output:\n0 never 1", result);
    }

    [Fact]
    public async Task RunShell_HostEnvironment_AddsVariablesAndRemovesThoseSetToNull()
    {
        ShellToolCollection collection = Collection(environment: new Dictionary<string, string?>
        {
            ["NO_COLOR"] = null,
            ["PINK_ROOSTER_TEST"] = "set"
        });

        string result = await collection.RunShell(IsWindows
            ? "Write-Output \"[$env:NO_COLOR] $env:PINK_ROOSTER_TEST\""
            : "echo \"[$NO_COLOR] $PINK_ROOSTER_TEST\"");

        Assert.EndsWith("Output:\n[] set", result);
    }

    [Fact]
    public async Task RunShell_PowerShell7_NamesItselfAndSupportsAndAnd()
    {
        if (FindOnPath(IsWindows ? "pwsh.exe" : "pwsh") is null)
        {
            return; // PowerShell 7 is a separate install.
        }

        string result = await Collection(shell: Shell.PowerShell).RunShell("Write-Output 'a' && Write-Output 'b'");

        Assert.Equal("Status: Exited\nExit Code: 0\nOutput:\na\nb", result);
    }

    [Fact]
    public async Task RunShell_CustomPosixShell_RunsTheCommandAndUsesItsName()
    {
        string bash = IsWindows ? @"C:\Program Files\Git\bin\bash.exe" : "/bin/sh";
        if (!File.Exists(bash))
        {
            return; // Git for Windows is not installed here.
        }

        string result = await Collection(shell: new Shell(ShellDialect.Posix, bash, "custom sh"))
            .RunShell("echo a && echo \"$GIT_TERMINAL_PROMPT\"");

        Assert.Equal("Status: Exited\nExit Code: 0\nOutput:\na\n0", result);
    }

    [Fact]
    public async Task RunShell_MissingShellExecutable_ReturnsStartFailedWithTheShellName()
    {
        string result = await Collection(shell: new Shell(ShellDialect.Posix, "no-such-shell-here")).RunShell("echo x");

        Assert.StartsWith("Status: StartFailed\nShell: no-such-shell-here\nError: ", result);
    }

    [Fact]
    public void Constructor_BudgetBelowMinimum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShellToolCollection(new Workspace(root), maxOutputCharacters: ShellToolCollection.MinOutputCharacters - 1));
    }

    [Fact]
    public void Constructor_MaxTimeoutShorterThanTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShellToolCollection(new Workspace(root), TimeSpan.FromSeconds(30), maxTimeout: TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Instructions_WindowsPowerShell_NamesItAndHowToChain()
    {
        ShellToolCollection collection = Collection(shell: Shell.WindowsPowerShell);

        Assert.Contains(collection.Instructions, instruction =>
            instruction.StartsWith("Commands run in Windows PowerShell 5.1") && instruction.Contains("&& and || don't exist"));
    }

    [Fact]
    public void Instructions_OtherShells_NameTheShellAndItsSyntax()
    {
        Assert.Contains("Commands run in PowerShell 7; write PowerShell syntax.", Collection(shell: Shell.PowerShell).Instructions);
        Assert.Contains("Commands run in bash, a POSIX shell; write sh syntax.",
            Collection(shell: new Shell(ShellDialect.Posix, "/bin/bash", "bash")).Instructions);
    }

    [Fact]
    public void Instructions_NameTheBaseDirectory()
    {
        Assert.Contains($"Commands start in the base directory {new Workspace(root).RootDirectory}; workingDirectory must stay inside it.", Collection().Instructions);
    }

    [Fact]
    public void Instructions_StateTheTimeoutsAndOutputLimitTheCollectionEnforces()
    {
        var collection = new ShellToolCollection(new Workspace(root), TimeSpan.FromSeconds(45), 1500, maxTimeout: TimeSpan.FromMinutes(5));

        Assert.Contains("Commands are stopped after 45 s unless timeoutSeconds asks for more (up to 300 s). A reply is limited to 1500 characters.",
            collection.Instructions);
    }

    [Fact]
    public void Description_NamesTheStatusesAndTheHostsPermissions()
    {
        string description = new ShellToolCollection(new Workspace(root)).GetAIFunctions().Single().Description;

        Assert.Contains("'TimedOut'", description);
        Assert.Contains("'StartFailed'", description);
        Assert.Contains("host's permissions", description);
    }

    [Fact]
    public void Constraints_WithoutPolicy_AreEmpty()
    {
        Assert.Empty(Collection().Constraints);
    }

    [Fact]
    public void Constraints_ListTheDenylistAndAllowlist()
    {
        ShellToolCollection collection = Collection(allowlist: ["git status", "dotnet"], denylist: ["rm"]);

        Assert.Equal(
        [
            "Commands starting with any of these are refused: 'rm'.",
            "Only commands starting with one of these run: 'git status', 'dotnet'. Run one command per call; chaining, grouping and redirection characters are refused."
        ], collection.Constraints);
    }

    private string MarkerPath => Path.Combine(root, "marker.txt");

    private ShellToolCollection Collection(
        TimeSpan? timeout = null,
        int? maxOutputCharacters = null,
        IReadOnlyList<string>? allowlist = null,
        IReadOnlyList<string>? denylist = null,
        Shell? shell = null,
        TimeSpan? maxTimeout = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool separateStreams = false,
        bool inheritEnvironment = true) =>
        new(new Workspace(root), timeout ?? TimeSpan.FromSeconds(20), maxOutputCharacters, allowlist, denylist, shell, maxTimeout, environment, separateStreams, inheritEnvironment);

    private static string Echo(string text) => IsWindows ? $"Write-Output '{text}'" : $"echo '{text}'";

    private static string Sleep(string before, int seconds) => IsWindows
        ? $"Write-Output '{before}'; Start-Sleep -Seconds {seconds}; Write-Output 'after'"
        : $"echo '{before}'; sleep {seconds}; echo after";

    private static string CreateMarker() => IsWindows ? "New-Item marker.txt" : "touch marker.txt";

    private static string FilterSample() => IsWindows
        ? "Write-Output 'Alpha one'; Write-Output 'beta two'; Write-Output 'ALPHA three'; " +
          "[Console]::Error.WriteLine('alpha err'); [Console]::Error.WriteLine('other err')"
        : "echo 'Alpha one'; echo 'beta two'; echo 'ALPHA three'; echo 'alpha err' >&2; echo 'other err' >&2";

    private static string NumberedLines(int count) => ShellCommands.NumberedLines(count);

    private static string? FindOnPath(string fileName) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(directory => Path.Combine(directory, fileName))
        .FirstOrDefault(File.Exists);
}
