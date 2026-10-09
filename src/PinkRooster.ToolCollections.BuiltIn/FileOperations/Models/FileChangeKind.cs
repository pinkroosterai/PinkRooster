namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>What a recorded <see cref="FileChange"/> did to its file.</summary>
internal enum FileChangeKind
{
    /// <summary>The content was edited or overwritten.</summary>
    Changed,

    Created,

    Moved,

    Deleted
}
