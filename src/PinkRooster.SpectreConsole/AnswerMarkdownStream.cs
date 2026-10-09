using System.Text;

namespace PinkRooster.SpectreConsole;

/// <summary>Applies the <see cref="AnswerMarkdown"/> fixes to an answer that arrives in pieces, holding text back only while it could still need one.</summary>
/// <remarks>
/// What is held: the blanks and the first characters of a line that could open a fence, a line that starts with <c># </c> (until the line ends,
/// because its heading and links are fixed together), a line of only <c>-</c> or only <c>*</c> (until it ends or gets another character, because it
/// could be a horizontal rule), and from an unfinished <c>[</c> to the end of the line or its <c>)</c>, plus a trailing <c>!</c>.
/// Fenced code is passed on at once. Everything else is passed on as it arrives, so the text it returns is what <see cref="AnswerMarkdown.Prepare"/> makes of the whole.
/// </remarks>
/// <param name="ruleWidth">The width of a horizontal rule: the width of the console the answer is drawn on.</param>
internal sealed class AnswerMarkdownStream(int ruleWidth)
{
    private string line = "";
    private int emitted;
    private bool inFence;
    private bool lineStartedInFence;
    private bool markerDecided;
    private bool isMarker;

    /// <summary>Takes the next piece and returns the text that is ready to draw.</summary>
    public string Push(string text)
    {
        StringBuilder output = new();
        int start = 0;
        while (true)
        {
            int lineEnd = text.IndexOf('\n', start);
            if (lineEnd < 0)
            {
                line += text[start..];
                Advance(output, complete: false);
                return output.ToString();
            }

            line += text[start..lineEnd];
            Advance(output, complete: true);
            output.Append('\n');
            StartLine();
            start = lineEnd + 1;
        }
    }

    /// <summary>Returns what is still held, at the end of the answer.</summary>
    public string Complete()
    {
        StringBuilder output = new();
        Advance(output, complete: true);
        StartLine();
        return output.ToString();
    }

    private void StartLine()
    {
        line = "";
        emitted = 0;
        lineStartedInFence = inFence;
        markerDecided = false;
        isMarker = false;
    }

    private void Advance(StringBuilder output, bool complete)
    {
        DecideMarker(complete);
        if (lineStartedInFence || (markerDecided && isMarker))
        {
            Emit(output, line.Length, rewrite: false);
            return;
        }
        if (!markerDecided)
        {
            return;
        }
        if (line.StartsWith("# ", StringComparison.Ordinal) || AnswerMarkdown.CouldBecomeRule(line))
        {
            // A line of one or two rule characters is no rule when it ends; FixLine then returns it as it is.
            if (complete)
            {
                output.Append(AnswerMarkdown.FixLine(line, ruleWidth));
                emitted = line.Length;
            }
            return;
        }
        if (line == "#" && !complete)
        {
            return;
        }

        int unfinishedLink = complete ? -1 : AnswerMarkdown.UnfinishedLinkStart(line);
        int safeEnd = unfinishedLink >= 0 ? unfinishedLink : line.Length;
        // A link that starts in the next piece is not an image only if no ! came just before it.
        if (!complete && safeEnd == line.Length && line.EndsWith('!'))
        {
            safeEnd--;
        }
        Emit(output, safeEnd, rewrite: true);
    }

    /// <summary>Settles whether the line is a fence marker as soon as its first non-blank characters say so; flips <see cref="inFence"/> once.</summary>
    private void DecideMarker(bool complete)
    {
        if (markerDecided)
        {
            return;
        }

        string trimmed = line.TrimStart();
        if (trimmed.Length >= 3)
        {
            markerDecided = true;
            if (AnswerMarkdown.IsFenceMarker(trimmed))
            {
                isMarker = true;
                inFence = !inFence;
            }
        }
        else if (complete || (trimmed.Length > 0 && !"```".StartsWith(trimmed, StringComparison.Ordinal) && !"~~~".StartsWith(trimmed, StringComparison.Ordinal)))
        {
            markerDecided = true;
        }
    }

    private void Emit(StringBuilder output, int end, bool rewrite)
    {
        if (end <= emitted)
        {
            return;
        }

        string piece = line[emitted..end];
        output.Append(rewrite ? AnswerMarkdown.RewriteLinks(piece) : piece);
        emitted = end;
    }
}
