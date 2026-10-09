namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>The changes the file tools made on one session, in order, and the undo unit new changes belong to.</summary>
internal sealed class FileChangeLog
{
    public int Unit { get; set; }

    public List<FileChange> Changes { get; set; } = [];
}
