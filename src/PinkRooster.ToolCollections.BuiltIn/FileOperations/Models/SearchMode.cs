namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>What <see cref="FileReadToolCollection.SearchFiles" /> answers with.</summary>
public enum SearchMode
{
    /// <summary>The paths of the files that contain a match, one per line.</summary>
    Files,

    /// <summary>Each matching line as <c>path:line: text</c>.</summary>
    Lines,

    /// <summary>The number of matching lines per file, and the total.</summary>
    Counts
}
