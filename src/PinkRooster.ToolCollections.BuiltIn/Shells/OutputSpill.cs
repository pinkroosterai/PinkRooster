using System.Text;
using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn.Shells;

/// <summary>
/// The file a long command output is saved to, in the workspace's <c>.pinkrooster/output</c> folder, so the model can read the
/// rest with the file tools. The folder holds its own <c>.gitignore</c>, and files older than a day, or beyond the newest
/// twenty, are removed when a new one is made. Nothing here throws: a failure is kept in <see cref="Error" />.
/// </summary>
internal sealed class OutputSpill(Workspace workspace)
{
    private const int KeptFiles = 20;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private readonly object gate = new();
    private StreamWriter? writer;

    /// <summary>Whether the file has been started.</summary>
    public bool IsOpen { get { lock (gate) { return writer is not null; } } }

    /// <summary>The path of the file relative to the workspace root, with <c>/</c> separators; null before it is started.</summary>
    public string? RelativePath { get; private set; }

    /// <summary>Why saving failed, or null.</summary>
    public string? Error { get; private set; }

    private string? fullPath;

    /// <summary>Creates the file and writes <paramref name="text" /> to it. Does nothing once started or failed.</summary>
    public void Start(string text)
    {
        lock (gate)
        {
            StartLocked(text);
        }
    }

    private void StartLocked(string text)
    {
        if (writer is not null || Error is not null)
        {
            return;
        }

        try
        {
            if (!ScratchFolder.TryPrepare(workspace, "output", out string directory, out string? unavailable))
            {
                Error = unavailable;
                return;
            }

            // Room for the file about to be made
            ScratchFolder.Prune(directory, "shell-*.txt", KeptFiles - 1, MaxAge);
            fullPath = Path.Combine(directory, $"shell-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.txt");
            writer = new StreamWriter(new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            RelativePath = Path.GetRelativePath(workspace.RootDirectory, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            writer.Write(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(ex);
        }
    }

    /// <summary>Writes more text to the started file.</summary>
    public void Append(string text)
    {
        lock (gate)
        {
            try
            {
                writer?.Write(text);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Fail(ex);
            }
        }
    }

    /// <summary>Closes the file; <paramref name="keep" /> false also deletes it.</summary>
    public void Close(bool keep)
    {
        lock (gate)
        {
            try
            {
                writer?.Dispose();
                writer = null;
                if (!keep && fullPath is not null)
                {
                    File.Delete(fullPath);
                    RelativePath = null;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Error ??= ex.Message;
            }
        }
    }

    private void Fail(Exception ex)
    {
        Error = ex.Message;
        writer?.Dispose();
        writer = null;
        RelativePath = null;
    }
}
