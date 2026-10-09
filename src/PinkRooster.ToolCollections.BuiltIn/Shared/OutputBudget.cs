namespace PinkRooster.ToolCollections.BuiltIn.Shared;

/// <summary>
/// How the built-in tools keep a reply within its size limit. The unit is characters throughout: it tracks what a reply
/// costs the model better than bytes or lines do, and it is the same for file reads, listings, searches and shell output.
/// </summary>
internal static class OutputBudget
{
    /// <summary>The start of a text, at most <paramref name="limit" /> characters.</summary>
    public static string CutToLength(string text, int limit) => text.Length <= limit ? text : text[..limit];

    /// <summary>
    /// Joins the start of <paramref name="start"/> and the end of <paramref name="end"/> around an omission marker so the
    /// result is at most <paramref name="budget"/> characters. <paramref name="total"/> is the length of the whole text, of
    /// which <paramref name="start"/> and <paramref name="end"/> are the known beginning and ending; pass the same string
    /// twice when the whole text is known.
    /// </summary>
    public static string MiddleTruncate(string start, string end, long total, int budget)
    {
        if (total <= budget && start.Length == total)
        {
            return start;
        }

        // Sized for the whole length: the omitted count can only be smaller, so the result never outgrows the budget.
        int markerLength = Marker(total).Length;
        int available = budget - markerLength;
        if (available <= 0)
        {
            string bare = Marker(total).Trim('\n');
            return bare.Length <= budget ? bare : string.Empty;
        }

        int headLength = Math.Min(start.Length, available / 2);
        int tailLength = Math.Min(end.Length, available - headLength);
        headLength = Math.Min(start.Length, available - tailLength);
        long omitted = total - headLength - tailLength;
        return start[..headLength] + Marker(omitted) + end[^tailLength..];
    }

    private static string Marker(long omitted) => $"\n[... {omitted} characters omitted ...]\n";
}
