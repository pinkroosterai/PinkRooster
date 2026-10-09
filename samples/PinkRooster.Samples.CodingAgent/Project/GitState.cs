using System.Diagnostics;

namespace PinkRooster.Samples.CodingAgent.Project;

/// <summary>What git says about the workspace, read by running <c>git</c>; everything is null or empty where git is absent or the workspace is not a repository.</summary>
public static class GitState
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    /// <summary>One line for the model, such as <c>branch main, 3 changed files</c>; null outside a repository.</summary>
    public static async Task<string?> DescribeAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        // The first line names the branch ("## main...origin/main"), every other line is one changed or untracked file.
        if (await RunAsync(workspaceRoot, cancellationToken, "status", "--porcelain=v1", "--branch").ConfigureAwait(false) is not string status)
        {
            return null;
        }
        string[] lines = status.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("## ", StringComparison.Ordinal))
        {
            return null;
        }
        string branch = lines[0][3..].Split("...")[0];
        int changed = lines.Length - 1;
        return $"branch {branch}, {changed} changed {(changed == 1 ? "file" : "files")}";
    }

    /// <summary>The working tree's changes as text: the diff of tracked files, then each untracked file as a new file. Null outside a repository; empty when nothing changed.</summary>
    public static async Task<string?> DiffAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        // A repository without a commit has no HEAD to compare with.
        string? tracked = await RunAsync(workspaceRoot, cancellationToken, "diff", "HEAD").ConfigureAwait(false)
            ?? await RunAsync(workspaceRoot, cancellationToken, "diff").ConfigureAwait(false);
        if (tracked is null)
        {
            return null;
        }
        string untracked = await RunAsync(workspaceRoot, cancellationToken, "ls-files", "--others", "--exclude-standard").ConfigureAwait(false) ?? string.Empty;

        List<string> parts = [];
        if (tracked.Trim().Length > 0)
        {
            parts.Add(tracked.TrimEnd());
        }
        foreach (string file in untracked.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            parts.Add($"new file: {file}");
        }
        return string.Join("\n", parts);
    }

    // The command's output, or null when git is not installed, the workspace is no repository, or the command failed or took too long.
    private static async Task<string?> RunAsync(string workspaceRoot, CancellationToken cancellationToken, params string[] arguments)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(Limit);
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(limit.Token);
                Task<string> errors = process.StandardError.ReadToEndAsync(limit.Token);
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                await errors.ConfigureAwait(false);
                return process.ExitCode == 0 ? await output.ConfigureAwait(false) : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No git on the path: the assistant works without the git line and /diff says so.
            return null;
        }
    }
}
