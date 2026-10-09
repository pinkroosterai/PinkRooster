
namespace PinkRooster.ToolCollections.BuiltIn.Shared;

/// <summary>
/// A folder under the workspace's <c>.pinkrooster</c> folder where the built-in tools keep files for the model and the host:
/// shell output too long for a reply, and backups. The <c>.pinkrooster</c> folder holds its own <c>.gitignore</c>, so none of it
/// reaches version control.
/// </summary>
internal static class ScratchFolder
{
    /// <summary>
    /// Creates <c>.pinkrooster/&lt;name&gt;</c> if needed, resolving it through the path guard so a link out of the workspace is refused
    /// instead of written through. <paramref name="error" /> is the reason, without the "Error:" prefix, when this returns false.
    /// </summary>
    public static bool TryPrepare(Workspace workspace, string name, out string directory, out string? error)
    {
        if (!PathGuard.TryResolvePath(workspace.RootDirectory, $".pinkrooster/{name}", out directory, out string? refused))
        {
            error = refused![7..];
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            string ignoreFile = Path.Combine(Path.GetDirectoryName(directory)!, ".gitignore");
            if (!File.Exists(ignoreFile))
            {
                File.WriteAllText(ignoreFile, "*\n");
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Removes files matching <paramref name="pattern" /> that are older than <paramref name="maxAge" />, then the oldest until at most <paramref name="keep" /> remain.</summary>
    public static void Prune(string directory, string pattern, int keep, TimeSpan maxAge)
    {
        List<FileInfo> files = new DirectoryInfo(directory).EnumerateFiles(pattern).OrderByDescending(file => file.LastWriteTimeUtc).ToList();
        for (int i = 0; i < files.Count; i++)
        {
            if (i >= keep || DateTime.UtcNow - files[i].LastWriteTimeUtc > maxAge)
            {
                try
                {
                    files[i].Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file still held open elsewhere stays; the next run tries again.
                }
            }
        }
    }
}
