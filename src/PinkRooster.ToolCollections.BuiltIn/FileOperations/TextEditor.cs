using System.Text;

namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>
/// Replaces one exact piece of text in a file's text. A line break in the target and the replacement matches, and is
/// written as, the file's own style; the lines outside the match keep theirs.
/// </summary>
internal static class TextEditor
{
    /// <param name="fileText">The file's text.</param>
    /// <param name="target">The text to replace.</param>
    /// <param name="replacement">The text that takes its place.</param>
    /// <param name="replaceAll">Replace every occurrence; otherwise the edit is applied only when there is exactly one.</param>
    public static EditResult Replace(string fileText, string target, string replacement, bool replaceAll = false)
    {
        string prevailingLineEnding = fileText.Contains("\r\n") ? "\r\n" : "\n";

        string normalizedFile = fileText.Replace("\r\n", "\n");
        string normalizedTarget = target.Replace("\r\n", "\n");
        string normalizedReplacement = replacement.Replace("\r\n", "\n");

        List<int> matchIndexes = new List<int>();
        int searchIndex = 0;
        while ((searchIndex = normalizedFile.IndexOf(normalizedTarget, searchIndex, StringComparison.Ordinal)) >= 0)
        {
            matchIndexes.Add(searchIndex);
            searchIndex += normalizedTarget.Length;
        }

        List<int> matchLines = [.. matchIndexes.Select(index => CountNewlines(normalizedFile.AsSpan(0, index)) + 1)];
        if (matchIndexes.Count == 0 || (matchIndexes.Count > 1 && !replaceAll))
        {
            return new EditResult(matchLines, null, 0, 0, 0);
        }

        int startLine = matchLines[0];

        // Only the matched spans are replaced in the file's own text, so the line endings of every other line stay as they were.
        // Going from the last match to the first keeps the earlier positions valid.
        string written = prevailingLineEnding == "\r\n"
            ? normalizedReplacement.Replace("\n", "\r\n")
            : normalizedReplacement;
        StringBuilder content = new StringBuilder(fileText);
        foreach (int matchIndex in Enumerable.Reverse(matchIndexes))
        {
            int rawStart = ToRawIndex(fileText, matchIndex);
            int rawEnd = ToRawIndex(fileText, matchIndex + normalizedTarget.Length);
            content.Remove(rawStart, rawEnd - rawStart).Insert(rawStart, written);
        }

        return new EditResult(
            matchLines,
            content.ToString(),
            startLine,
            startLine + LinesSpanned(normalizedTarget),
            startLine + LinesSpanned(normalizedReplacement));
    }

    /// <summary>
    /// The lines of the file most like the first non-blank line of <paramref name="target" />, best first, for a reply to a
    /// target that was not found. Lines are compared ignoring differences in white space; a line that is not alike enough is left out.
    /// </summary>
    public static IReadOnlyList<NearbyLine> NearestLines(string fileText, string target, int count = 3)
    {
        string? key = target.Replace("\r\n", "\n").Split('\n').Select(CollapseWhiteSpace).FirstOrDefault(line => line.Length > 0);
        if (key is null)
        {
            return [];
        }

        Dictionary<string, int> keyBigrams = Bigrams(key);
        List<(NearbyLine Line, double Score)> scored = new List<(NearbyLine Line, double Score)>();
        string[] lines = fileText.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string collapsed = CollapseWhiteSpace(lines[i]);
            if (collapsed.Length == 0)
            {
                continue;
            }

            double score = Dice(keyBigrams, key, Bigrams(collapsed), collapsed);
            if (score >= MinimumSimilarity)
            {
                scored.Add((new NearbyLine(i + 1, lines[i].TrimEnd()), score));
            }
        }

        return [.. scored.OrderByDescending(item => item.Score).ThenBy(item => item.Line.Number).Take(count).Select(item => item.Line)];
    }

    private const double MinimumSimilarity = 0.5;

    private static string CollapseWhiteSpace(string line) => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Dictionary<string, int> Bigrams(string text)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        for (int i = 0; i + 1 < text.Length; i++)
        {
            string pair = text.Substring(i, 2);
            counts[pair] = counts.GetValueOrDefault(pair) + 1;
        }

        return counts;
    }

    // The Dice coefficient of two texts' character pairs; texts too short for a pair are alike only when equal.
    private static double Dice(Dictionary<string, int> a, string textA, Dictionary<string, int> b, string textB)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return string.Equals(textA, textB, StringComparison.Ordinal) ? 1 : 0;
        }

        int shared = a.Sum(pair => Math.Min(pair.Value, b.GetValueOrDefault(pair.Key)));
        return 2.0 * shared / (textA.Length - 1 + textB.Length - 1);
    }

    // The index in the file's own text of a position in its text with every CRLF read as LF.
    private static int ToRawIndex(string rawText, int normalizedIndex)
    {
        int normalized = 0;
        for (int i = 0; i < rawText.Length; i++)
        {
            if (normalized == normalizedIndex)
            {
                return i;
            }

            if (rawText[i] == '\r' && i + 1 < rawText.Length && rawText[i + 1] == '\n')
            {
                continue;
            }

            normalized++;
        }

        return rawText.Length;
    }

    // The number of lines after the first that a text reaches into; a line break that ends the text opens no further line.
    private static int LinesSpanned(string text) => CountNewlines(text) - (text.EndsWith('\n') ? 1 : 0);

    private static int CountNewlines(ReadOnlySpan<char> text) => text.Count('\n');
}
