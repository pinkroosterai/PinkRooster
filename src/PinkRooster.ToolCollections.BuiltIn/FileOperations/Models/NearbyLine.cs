namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>A line of a file shown back to the model, with its 1-based line number.</summary>
internal sealed record NearbyLine(int Number, string Text);
