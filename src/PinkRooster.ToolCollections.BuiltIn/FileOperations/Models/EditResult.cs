namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>What <see cref="TextEditor.Replace" /> found. Line numbers start at 1 and count from the file's text with every line break read as LF.</summary>
/// <param name="MatchLines">The first line of every place the target occurs.</param>
/// <param name="Content">The file's text after the edit; null when nothing was replaced (no match, or several without replace-all).</param>
/// <param name="StartLine">The line the first replaced text began on.</param>
/// <param name="EndLine">The last line the replaced text covered.</param>
/// <param name="NewEndLine">The last line the replacement covers.</param>
internal sealed record EditResult(IReadOnlyList<int> MatchLines, string? Content, int StartLine, int EndLine, int NewEndLine);
