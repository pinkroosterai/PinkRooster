namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>One file of a <see cref="FileUndoResult"/>.</summary>
/// <param name="Path">The file, relative to the workspace root with forward slashes.</param>
/// <param name="Detail">What undo did to it, or why it was left as it is, such as <c>its previous content was put back</c>.</param>
public sealed record FileUndoEntry(string Path, string Detail);
