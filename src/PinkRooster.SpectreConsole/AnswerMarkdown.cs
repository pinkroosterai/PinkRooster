using System.Text.RegularExpressions;

namespace PinkRooster.SpectreConsole;

/// <summary>Rewrites the three things NTokenizers 2.4.0 draws badly, so the answer reads well on a terminal.</summary>
/// <remarks>
/// A level 1 heading is drawn as <c>** Title **</c> over a rule, with the asterisks literal, and a link's text is dropped, leaving
/// <c>[](url)</c>; both seen with ANSI on. <c>MarkdownStyles</c> only sets colours, so
/// the text is changed before it is drawn: <c># Title</c> becomes <c>## Title</c>, which is drawn without asterisks, and
/// <c>[text](url)</c> becomes <c>text (url)</c>. A horizontal rule (<c>---</c> or <c>***</c>) is drawn as wide as <c>System.Console.WindowWidth</c>,
/// not as wide as the console it is drawn on, and that property throws on Windows when the process has no console, on a thread nobody can
/// catch it on; so the rule is drawn here, as a line of <c>─</c> of the console's own width, and the package never sees one.
/// Fenced code is left alone. Remove this when the package draws all three correctly.
/// </remarks>
internal static partial class AnswerMarkdown
{
    [GeneratedRegex(@"(?<!!)\[(?<text>[^\]\r\n]+)\]\((?<url>[^)\s]+)\)")]
    private static partial Regex Link();

    /// <param name="markdown">The whole answer.</param>
    /// <param name="ruleWidth">The width of a horizontal rule: the width of the console the answer is drawn on.</param>
    public static string Prepare(string markdown, int ruleWidth)
    {
        string[] lines = markdown.Split('\n');
        bool inFence = false;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (IsFenceMarker(line.TrimStart()))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
            {
                continue;
            }

            lines[index] = FixLine(line, ruleWidth);
        }
        return string.Join('\n', lines);
    }

    /// <summary>Rewrites one whole line outside fenced code: a horizontal rule, or a level 1 heading and the links in it.</summary>
    public static string FixLine(string line, int ruleWidth) => IsRule(line)
        ? new string('─', Math.Max(1, ruleWidth))
        : RewriteLinks(line.StartsWith("# ", StringComparison.Ordinal) ? "#" + line : line);

    /// <summary>True for a whole line the package draws as a horizontal rule: three or more <c>-</c>, or three or more <c>*</c>, after any leading blanks and with nothing behind them.</summary>
    public static bool IsRule(string line) => RuleMarks(line) >= 3;

    /// <summary>True for the start of a line that more text could still turn into a horizontal rule.</summary>
    public static bool CouldBecomeRule(string line) => RuleMarks(line) >= 1;

    /// <summary>The number of rule characters in a line that holds nothing else but leading blanks and a carriage return at its end; 0 for any other line.</summary>
    private static int RuleMarks(string line)
    {
        ReadOnlySpan<char> marks = line.AsSpan().TrimStart();
        if (marks.EndsWith("\r"))
        {
            marks = marks[..^1];
        }
        if (marks.IsEmpty || marks[0] is not ('-' or '*') || marks.ContainsAnyExcept(marks[0]))
        {
            return 0;
        }
        return marks.Length;
    }

    /// <summary>Rewrites the links in text that does not cut one in two and does not end in <c>!</c> (see <see cref="UnfinishedLinkStart"/>).</summary>
    public static string RewriteLinks(string text) => Link().Replace(text, "${text} (${url})");

    /// <summary>True for a line, without its leading blanks, that opens or closes a fenced code block.</summary>
    public static bool IsFenceMarker(string trimmedLine) =>
        trimmedLine.StartsWith("```", StringComparison.Ordinal) || trimmedLine.StartsWith("~~~", StringComparison.Ordinal);

    /// <summary>The index of the first <c>[</c> in one line that more text could still turn into a link, or -1.</summary>
    /// <remarks>A <c>[</c> after <c>!</c> is an image, not a link. Matches the shape <c>[text](url)</c> of <see cref="Link"/>: text without <c>]</c> or a line break, url without <c>)</c> or blanks, each at least one character.</remarks>
    public static int UnfinishedLinkStart(string line)
    {
        for (int start = line.IndexOf('['); start >= 0; start = line.IndexOf('[', start + 1))
        {
            if ((start == 0 || line[start - 1] != '!') && CouldBecomeLink(line, start))
            {
                return start;
            }
        }
        return -1;
    }

    private static bool CouldBecomeLink(string line, int start)
    {
        int index = start + 1;
        while (index < line.Length && line[index] != ']')
        {
            if (line[index] is '\r' or '\n')
            {
                return false;
            }
            index++;
        }
        if (index == line.Length)
        {
            return true;
        }
        if (index == start + 1)
        {
            return false;
        }
        index++;
        if (index == line.Length)
        {
            return true;
        }
        if (line[index] != '(')
        {
            return false;
        }
        int urlStart = ++index;
        while (index < line.Length && line[index] != ')')
        {
            if (char.IsWhiteSpace(line[index]))
            {
                return false;
            }
            index++;
        }
        return index == line.Length;
    }
}
