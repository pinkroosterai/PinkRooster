namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>One change a file tool made, with what undo needs to take it back. Kept in the session, so it is small and plain JSON.</summary>
internal sealed class FileChange
{
    /// <summary>The undo unit the change belongs to: the user message it was made for.</summary>
    public int Unit { get; set; }

    public FileChangeKind Kind { get; set; }

    /// <summary>The file as it is after the change, relative to the workspace root with forward slashes; for a delete, the file that is gone.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Where a moved file came from, relative to the workspace root.</summary>
    public string? From { get; set; }

    /// <summary>The file name of the copy in <c>.pinkrooster/backups</c> that holds the previous content; null when none was kept.</summary>
    public string? Backup { get; set; }

    /// <summary>The SHA-256 of the file as the tool left it, by which undo sees that someone else changed it since; null when it could not be read.</summary>
    public string? After { get; set; }
}
