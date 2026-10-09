using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI;
using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>
/// Provides an agent with fine-grained, atomic file operations strictly sandboxed to a workspace: everything
/// <see cref="FileReadToolCollection" /> does, plus creating, overwriting, editing, moving and deleting files.
/// </summary>
public sealed class FileOperationsToolCollection : FileReadToolCollection
{
    // The edited lines an EditFile reply shows back.
    private const int MaxEchoedLines = 20;

    private const int KeptBackups = 100;
    private static readonly TimeSpan MaxBackupAge = TimeSpan.FromDays(7);

    private readonly bool keepBackups;

    /// <summary>Creates a file collection sandboxed to the workspace's root directory, with the default read limit and no backups.</summary>
    /// <remarks>For another read limit or for backups use <see cref="FileOperationsToolCollectionBuilder"/>.</remarks>
    /// <param name="workspace">The workspace whose root directory all operations are sandboxed within; share it with the shell collection.</param>
    public FileOperationsToolCollection(Workspace workspace) : this(workspace, DefaultMaxReadCharacters)
    {
    }

    /// <summary>
    /// Creates a new <see cref="FileOperationsToolCollection"/> sandboxed to the workspace's root directory.
    /// </summary>
    /// <param name="workspace">The workspace whose root directory all operations are sandboxed within; share it with the shell collection.</param>
    /// <param name="maxReadCharacters">The most characters a read, a listing or a search returns in one reply (defaults to 51,200).</param>
    /// <param name="keepBackups">
    /// <c>true</c> to copy a file's content to <c>.pinkrooster/backups</c> before <c>EditFile</c>, <c>WriteFile</c> or <c>DeleteFile</c>
    /// changes or removes it, so a change made without the host's approval can be undone, by hand or with <see cref="UndoAsync"/>. The reply names the copy. Backups older
    /// than a week, or beyond the newest hundred, are removed when a new one is made. If a copy cannot be made, the tool changes nothing.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxReadCharacters"/> is less than or equal to zero.</exception>
    internal FileOperationsToolCollection(Workspace workspace, int maxReadCharacters = DefaultMaxReadCharacters, bool keepBackups = false)
        : base(workspace, maxReadCharacters)
    {
        this.keepBackups = keepBackups;
        if (keepBackups)
        {
            AddInstruction("Before a file is edited, overwritten or deleted, a copy of its previous content is kept in .pinkrooster/backups; the reply names it.");
        }
    }

    /// <summary>
    /// Creates a new file with the specified content inside the base directory.
    /// </summary>
    [Tool("CreateFile",
        "Creates a new file with the given content, and any missing parent directories. " +
        "It never overwrites: if the file exists it fails; use EditFile to change part of it or WriteFile to replace all of it.",
        Kind = ToolKind.Edit)]
    public Task<string> CreateFile(
        [Description("The file to create.")]
        string path,
        [Description("The complete text of the new file.")]
        string content,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunAsync(async () =>
        {
            if (!PathGuard.TryResolvePath(baseDirectory, path, out string canonicalPath, out string? pathError))
            {
                return pathError!;
            }

            if (File.Exists(canonicalPath))
            {
                return $"Error: File already exists at '{path}'. Use EditFile to modify it or WriteFile to overwrite it.";
            }

            if (Directory.Exists(canonicalPath))
            {
                return $"Error: A directory already exists at '{path}'.";
            }

            EnsureParentDirectoryExists(canonicalPath);

            // CreateNew fails when the file appeared after the check above, so a create never overwrites
            FileStream stream;
            try
            {
                stream = new FileStream(canonicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
            }
            catch (IOException) when (File.Exists(canonicalPath))
            {
                return $"Error: File already exists at '{path}'. Use EditFile to modify it or WriteFile to overwrite it.";
            }

            byte[] bytes = TextFile.Utf8.GetBytes(content);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }

            Record(FileChangeKind.Created, canonicalPath);
            return $"File created successfully: '{path}' ({bytes.Length} bytes).";
        });
    }

    /// <summary>
    /// Overwrites the entire content of an existing file within the base directory. Requires host approval.
    /// </summary>
    [Tool("WriteFile",
        "Replaces the entire content of an existing file, without needing its old text. " +
        "To change only part of a file use EditFile; it fails if the file does not exist, so use CreateFile for a new file.",
        RequiresApproval = true,
        Kind = ToolKind.Edit)]
    public Task<string> WriteFile(
        [Description("The existing file to overwrite.")]
        string path,
        [Description("The complete new text of the file.")]
        string content,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunAsync(async () =>
        {
            if (!TryResolveExistingFile(path, ifMissing: "Use CreateFile to create a new file.", out string canonicalPath, out string? pathError))
            {
                return pathError!;
            }

            if (!TryKeepBackup(canonicalPath, path, out string backup, out string? copy, out string? backupError))
            {
                return backupError!;
            }

            // The file keeps the byte order mark it had
            await File.WriteAllTextAsync(canonicalPath, content, TextFile.EncodingToWrite(canonicalPath), cancellationToken).ConfigureAwait(false);
            Record(FileChangeKind.Changed, canonicalPath, copy);
            int bytesWritten = Encoding.UTF8.GetByteCount(content);
            return $"File overwritten successfully: '{path}' ({bytesWritten} bytes).{backup}";
        });
    }

    /// <summary>
    /// Replaces an exact, unique target text snippet in an existing file.
    /// </summary>
    [Tool("EditFile",
        "Replaces one piece of text in an existing file and returns the lines it changed. " +
        "oldText must match the file exactly, indentation included, and occur once: add neighbouring lines to make it unique, or set replaceAll. " +
        "Copy oldText from ReadFile without the 'N: ' prefixes. To replace a whole file use WriteFile.",
        Kind = ToolKind.Edit)]
    public Task<string> EditFile(
        [Description("The file to edit.")]
        string path,
        [Description("The exact text to replace.")]
        string oldText,
        [Description("The replacement text. Empty deletes oldText.")]
        string newText,
        [Description("True to replace every occurrence of oldText instead of requiring exactly one.")]
        bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunAsync(async () =>
        {
            if (string.IsNullOrEmpty(oldText))
            {
                return "Error: oldText cannot be empty.";
            }

            if (string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                return "Error: oldText and newText are identical; no changes to make.";
            }

            if (!TryResolveExistingFile(path, ifMissing: null, out string canonicalPath, out string? pathError))
            {
                return pathError!;
            }

            byte[] fileBytes = await File.ReadAllBytesAsync(canonicalPath, cancellationToken).ConfigureAwait(false);
            if (!TextFile.TryDecode(fileBytes, out string fileText, out bool hasByteOrderMark))
            {
                return $"Error: File '{path}' contains binary or non-UTF-8 content and cannot be edited.";
            }

            EditResult edit = TextEditor.Replace(fileText, oldText, newText, replaceAll);
            if (edit.MatchLines.Count == 0)
            {
                IReadOnlyList<NearbyLine> nearby = TextEditor.NearestLines(fileText, oldText);
                if (nearby.Count == 0)
                {
                    return $"Error: oldText was not found in '{path}'. Read the file lines again to verify the exact text.";
                }

                return $"Error: oldText was not found in '{path}'. The closest lines in the file are:\n" +
                       string.Join("\n", nearby.Select(line => $"{line.Number}: {line.Text}")) +
                       "\nCopy the text exactly from ReadFile, leaving out the 'N: ' prefixes.";
            }

            if (edit.Content is null)
            {
                string lineList = string.Join(", ", edit.MatchLines.Select(l => $"line {l}"));
                return $"Error: oldText matched {edit.MatchLines.Count} times in '{path}' at {lineList}. Add surrounding lines to oldText to make it unique, or set replaceAll to replace every occurrence.";
            }

            if (!TryKeepBackup(canonicalPath, path, out string backup, out string? copy, out string? backupError))
            {
                return backupError!;
            }

            await File.WriteAllTextAsync(canonicalPath, edit.Content, hasByteOrderMark ? TextFile.Utf8WithBom : TextFile.Utf8, cancellationToken).ConfigureAwait(false);
            Record(FileChangeKind.Changed, canonicalPath, copy);

            if (edit.MatchLines.Count > 1)
            {
                return $"Successfully edited '{path}'. Replaced {edit.MatchLines.Count} occurrences at {string.Join(", ", edit.MatchLines.Select(l => $"line {l}"))}.{backup}";
            }

            string[] newLines = edit.Content.Replace("\r\n", "\n").Split('\n');
            int shownFrom = edit.StartLine;
            int shownTo = Math.Min(edit.NewEndLine, edit.StartLine + MaxEchoedLines - 1);
            string echoed = string.Join("\n", Enumerable.Range(shownFrom, shownTo - shownFrom + 1).Select(n => $"{n}: {newLines[n - 1]}"));
            string more = shownTo < edit.NewEndLine ? $"\n[... {edit.NewEndLine - shownTo} more lines]" : string.Empty;
            return $"Successfully edited '{path}'. Replaced lines {edit.StartLine}-{edit.EndLine} (now lines {edit.StartLine}-{edit.NewEndLine}):\n{echoed}{more}{backup}";
        });
    }

    /// <summary>
    /// Moves or renames a file within the base directory.
    /// </summary>
    [Tool("MoveFile",
        "Moves or renames one file, creating any missing parent directories of the destination. " +
        "It never overwrites: it fails if the destination exists. Directories cannot be moved.",
        Kind = ToolKind.Edit)]
    public Task<string> MoveFile(
        [Description("The existing file to move.")]
        string sourcePath,
        [Description("The new path of the file, including its file name.")]
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunOnPoolAsync(() =>
        {
            if (!TryResolveExistingFile(sourcePath, ifMissing: null, out string canonicalSource, out string? sourceError, followFinalLink: false))
            {
                return sourceError!;
            }

            if (!PathGuard.TryResolvePath(baseDirectory, destinationPath, out string canonicalDest, out string? destError, followFinalLink: false))
            {
                return destError!;
            }

            if (File.Exists(canonicalDest))
            {
                return $"Error: Destination file already exists: '{destinationPath}'. MoveFile will not overwrite existing files.";
            }

            if (Directory.Exists(canonicalDest))
            {
                return $"Error: Destination path '{destinationPath}' is an existing directory. Specify a full destination file path.";
            }

            EnsureParentDirectoryExists(canonicalDest);

            File.Move(canonicalSource, canonicalDest);
            Record(FileChangeKind.Moved, canonicalDest, from: canonicalSource);
            return $"Successfully moved '{sourcePath}' to '{destinationPath}'.";
        }, cancellationToken);
    }

    /// <summary>
    /// Deletes an existing file permanently within the base directory. Requires host approval.
    /// </summary>
    [Tool("DeleteFile",
        "Permanently deletes one file; there is no undo. Directories are refused.",
        RequiresApproval = true,
        Kind = ToolKind.Edit)]
    public Task<string> DeleteFile(
        [Description("The file to delete.")]
        string path,
        CancellationToken cancellationToken = default)
    {
        return ToolErrors.RunOnPoolAsync(() =>
        {
            if (!TryResolveExistingFile(path, ifMissing: null, out string canonicalPath, out string? error, followFinalLink: false))
            {
                return error!;
            }

            if (!TryKeepBackup(canonicalPath, path, out string backup, out string? copy, out string? backupError))
            {
                return backupError!;
            }

            File.Delete(canonicalPath);
            Record(FileChangeKind.Deleted, canonicalPath, copy);
            return $"Successfully deleted file '{path}'.{backup}";
        }, cancellationToken);
    }

    // Copies the file's current content to .pinkrooster/backups when backups are on; backup is then the sentence naming the copy,
    // or empty, and copyName the copy's file name, or null. A copy that cannot be made is an error: the change is not made without it.
    private bool TryKeepBackup(string canonicalPath, string path, out string backup, out string? copyName, out string? error)
    {
        backup = string.Empty;
        copyName = null;
        error = null;
        if (!keepBackups || new FileInfo(canonicalPath).LinkTarget is not null)
        {
            // A link has no content of its own to keep: deleting it leaves its target alone
            return true;
        }

        try
        {
            if (!ScratchFolder.TryPrepare(workspace, "backups", out string directory, out string? unavailable))
            {
                error = $"Error: Could not keep a backup, so '{path}' was not changed: {unavailable}";
                return false;
            }

            ScratchFolder.Prune(directory, "*.bak", KeptBackups - 1, MaxBackupAge);
            string name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}-{path.Replace('\\', '/').Replace("/", "__")}.bak";
            string copy = Path.Combine(directory, name);
            File.Copy(canonicalPath, copy);
            copyName = name;
            backup = $" Previous content kept in {Path.GetRelativePath(baseDirectory, copy).Replace(Path.DirectorySeparatorChar, '/')}.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Error: Could not keep a backup, so '{path}' was not changed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Marks where a user message begins: the file changes made on <paramref name="session"/> from here on are one undo unit, which
    /// one <see cref="UndoAsync"/> takes back together. Call it before each prompt you send, not for a message posted to a run in progress.
    /// </summary>
    /// <remarks>Without this call every change of the session is one unit. It does nothing on a collection without backups.</remarks>
    /// <param name="session">The session the prompt is sent on.</param>
    public void BeginUndoUnit(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!keepBackups)
        {
            return;
        }

        lock (session.StateBag)
        {
            FileChangeLog log = SessionState<FileChangeLog>(session) ?? new FileChangeLog();
            log.Unit++;
            SetSessionState(session, log);
        }
    }

    /// <summary>
    /// Takes back every file change the tools made on <paramref name="session"/> for the last user message that changed a file: an
    /// edited, overwritten or deleted file gets its previous content back byte for byte, a created file is removed, a moved file is
    /// moved back. The next call takes back the message before that. There is no redo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file whose content is no longer what the tool left is left as it is and named in <see cref="FileUndoResult.Skipped"/>, and so is
    /// a file whose copy in <c>.pinkrooster/backups</c> is gone; the other files are restored. Only what the file tools did is taken
    /// back: what a shell command changed stays, and directories a tool created stay.
    /// </para>
    /// <para>
    /// Call it between runs, as a host command; it is not a tool for the model. The conversation is not rewound, so put the result's
    /// text in front of the next prompt. The record is kept in the session, so undo also works after the session was saved and restored.
    /// </para>
    /// </remarks>
    /// <param name="session">The session whose changes to take back.</param>
    /// <param name="cancellationToken">Cancels between files; a file is never left half restored.</param>
    /// <exception cref="InvalidOperationException">The collection keeps no backups, so there is nothing to restore from; the message names the fix.</exception>
    public async Task<FileUndoResult> UndoAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!keepBackups)
        {
            throw new InvalidOperationException(
                "Undo needs the backups this collection was built without. Build it with new FileOperationsToolCollectionBuilder().InWorkspace(workspace).KeepBackups().Build().");
        }

        List<FileChange> unit;
        lock (session.StateBag)
        {
            FileChangeLog? log = SessionState<FileChangeLog>(session);
            if (log is null || log.Changes.Count == 0)
            {
                return new FileUndoResult([], []);
            }

            // The changes leave the record before they are taken back: a file that is skipped is not tried again by the next undo.
            int last = log.Changes.Max(change => change.Unit);
            unit = [.. log.Changes.Where(change => change.Unit == last)];
            log.Changes.RemoveAll(change => change.Unit == last);
            SetSessionState(session, log);
        }

        // Newest first, so a file changed twice goes back through its states in order. A file that could not be taken back one step
        // stays where it is for the steps before it, and is reported once, with the first reason.
        Dictionary<string, string> restored = new(StringComparer.Ordinal);
        Dictionary<string, string> skipped = new(StringComparer.Ordinal);
        for (int i = unit.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileChange change = unit[i];
            string reported = change.Kind == FileChangeKind.Moved ? change.From! : change.Path;
            string? problem;
            string done;
            try
            {
                (problem, done) = await TakeBackAsync(change).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                (problem, done) = (ex.Message, string.Empty);
            }

            if (problem is null)
            {
                // A file that was moved is reported under the name it has again.
                restored.Remove(change.Path);
                restored[reported] = done;
            }
            else
            {
                skipped.TryAdd(change.Path, problem);
            }
        }

        foreach (string path in skipped.Keys)
        {
            restored.Remove(path);
        }
        return new FileUndoResult(
            [.. restored.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new FileUndoEntry(entry.Key, entry.Value))],
            [.. skipped.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => new FileUndoEntry(entry.Key, entry.Value))]);
    }

    // Takes one change back. Returns why it could not, or null and what was done.
    private async Task<(string? Problem, string Done)> TakeBackAsync(FileChange change)
    {
        bool followLink = change.Kind is FileChangeKind.Changed or FileChangeKind.Created;
        if (!PathGuard.TryResolvePath(baseDirectory, change.Path, out string path, out string? refused, followLink))
        {
            return (refused![7..], string.Empty);
        }

        if (change.Kind == FileChangeKind.Deleted)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                return ("something else is at this path now", string.Empty);
            }
            if (BackupOf(change) is not string deleted)
            {
                return (NoCopy(change), string.Empty);
            }
            EnsureParentDirectoryExists(path);
            await File.WriteAllBytesAsync(path, await File.ReadAllBytesAsync(deleted).ConfigureAwait(false)).ConfigureAwait(false);
            return (null, "put back; it had been deleted");
        }

        if (!File.Exists(path))
        {
            return ("it is no longer there", string.Empty);
        }
        if (change.After is not null && !string.Equals(HashOf(path), change.After, StringComparison.Ordinal))
        {
            return ("it was changed by someone else since", string.Empty);
        }

        switch (change.Kind)
        {
            case FileChangeKind.Created:
                File.Delete(path);
                return (null, "removed; it had been created");

            case FileChangeKind.Moved:
                if (!PathGuard.TryResolvePath(baseDirectory, change.From!, out string source, out string? sourceRefused, followFinalLink: false))
                {
                    return (sourceRefused![7..], string.Empty);
                }
                if (File.Exists(source) || Directory.Exists(source))
                {
                    return ($"something else is at '{change.From}' now, where it came from", string.Empty);
                }
                EnsureParentDirectoryExists(source);
                File.Move(path, source);
                return (null, $"moved back from '{change.Path}'");

            default:
                if (BackupOf(change) is not string previous)
                {
                    return (NoCopy(change), string.Empty);
                }
                // The bytes of the copy, so line endings and a byte order mark come back as they were.
                await File.WriteAllBytesAsync(path, await File.ReadAllBytesAsync(previous).ConfigureAwait(false)).ConfigureAwait(false);
                return (null, "its previous content was put back");
        }
    }

    // The copy that holds the change's previous content, or null when none was kept or it is gone.
    private string? BackupOf(FileChange change) =>
        change.Backup is not null
        && PathGuard.TryResolvePath(baseDirectory, $".pinkrooster/backups/{change.Backup}", out string copy, out _)
        && File.Exists(copy)
            ? copy
            : null;

    private static string NoCopy(FileChange change) => change.Backup is null
        ? "no copy of its previous content was kept"
        : "the copy of its previous content is gone (copies are kept for a week, and the newest hundred)";

    // Adds a change to the record of the session of the run in progress. A tool called outside a run has no session, and without
    // backups there is nothing to undo with, so neither records.
    private void Record(FileChangeKind kind, string canonicalPath, string? backup = null, string? from = null)
    {
        if (!keepBackups || AIAgent.CurrentRunContext?.Session is not AgentSession session)
        {
            return;
        }

        FileChange change = new()
        {
            Kind = kind,
            Path = Relative(canonicalPath),
            From = from is null ? null : Relative(from),
            Backup = backup,
            After = kind == FileChangeKind.Deleted ? null : HashOf(canonicalPath)
        };
        // Two tool calls of one model reply can run at the same time.
        lock (session.StateBag)
        {
            FileChangeLog log = SessionState<FileChangeLog>(session) ?? new FileChangeLog();
            change.Unit = log.Unit;
            log.Changes.Add(change);
            SetSessionState(session, log);
        }
    }

    private string Relative(string canonicalPath) => Path.GetRelativePath(baseDirectory, canonicalPath).Replace(Path.DirectorySeparatorChar, '/');

    // Null when the file cannot be read, such as a link whose target is gone; undo then does not compare.
    private static string? HashOf(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void EnsureParentDirectoryExists(string filePath)
    {
        string? parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }
}
