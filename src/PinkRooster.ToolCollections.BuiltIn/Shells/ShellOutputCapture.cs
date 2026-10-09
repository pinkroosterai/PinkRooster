using System.Text;
using System.Text.RegularExpressions;
using PinkRooster.ToolCollections.BuiltIn.Shared;

namespace PinkRooster.ToolCollections.BuiltIn.Shells;

/// <summary>
/// Reads redirected output streams of a shell process line by line, keeping only the lines that match the filters and
/// only as much of them as the output budget can use: a head and a rolling tail of at most <c>limit</c> characters each.
/// Memory stays bounded however much the command prints; what falls between head and tail is counted, not kept.
/// Several streams can feed one capture: their lines are kept in the order they arrive.
/// </summary>
/// <remarks>
/// A line longer than <c>limit</c> is cut and says how much was cut. Terminal escape sequences (colours, cursor moves, window
/// titles) are removed before filtering: the model can't see them and they cost tokens. Reading and rendering may overlap
/// when the tool stops waiting for a stream a leftover process still holds, so both take the same lock. With a spill, the
/// kept lines are also written to a file once they come near the limit, so the whole of a long output stays reachable.
/// </remarks>
internal sealed partial class ShellOutputCapture(IReadOnlyList<string> filters, int limit, OutputSpill? spill = null)
{
    // Where saving starts: leaving room below the limit for the result's header, so output that gets cut is already on disk.
    private readonly int spillThreshold = Math.Max(limit / 2, limit - 1000);

    // CSI sequences (ESC [ ... final byte), OSC sequences (ESC ] ... BEL or ESC \), and two-character ESC sequences.
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[@-Z\\-_])")]
    private static partial Regex TerminalEscape();

    private readonly object gate = new();
    private readonly StringBuilder head = new();
    private readonly Queue<string> tail = new();
    private bool headClosed;
    private int tailLength;
    private long keptCharacters;
    private long omittedCharacters;
    private int totalLines;
    private int matchedLines;
    private string? readError;

    /// <summary>Every line the stream produced, matching or not.</summary>
    public int TotalLines
    {
        get { lock (gate) { return totalLines; } }
    }

    /// <summary>The lines that passed the filters; all lines when there are no filters.</summary>
    public int MatchedLines
    {
        get { lock (gate) { return matchedLines; } }
    }

    /// <summary>Why reading stopped before the end of the stream, or null when it did not.</summary>
    public string? ReadError
    {
        get { lock (gate) { return readError; } }
    }

    /// <summary>Whether any matching text was captured.</summary>
    public bool HasText
    {
        get { lock (gate) { return keptCharacters > 0; } }
    }

    /// <summary>The number of matching characters, including those left out between head and tail.</summary>
    public long Length
    {
        get { lock (gate) { return keptCharacters; } }
    }

    /// <summary>The spill this capture writes to, once it has been started.</summary>
    public OutputSpill? Spill => spill;

    /// <summary>Reads <paramref name="reader"/> to its end.</summary>
    public async Task ReadAsync(TextReader reader, CancellationToken cancellationToken)
    {
        LineState current = new LineState();
        char[] buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                lock (gate)
                {
                    for (int i = 0; i < read; i++)
                    {
                        Accept(current, buffer[i]);
                    }
                }
            }

            lock (gate)
            {
                if (current.Text.Length > 0 || current.Cut > 0)
                {
                    EndLine(current);
                }
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The result reports this, so output that stops early is never mistaken for the whole output.
            lock (gate)
            {
                readError = error.Message;
            }
        }
    }

    /// <summary>The captured text, middle-truncated to at most <paramref name="budget"/> characters.</summary>
    public string Render(int budget)
    {
        lock (gate)
        {
            string headText = head.ToString();
            string tailText = string.Concat(tail);
            string text = omittedCharacters == 0
                ? OutputBudget.MiddleTruncate(headText + tailText, headText + tailText, keptCharacters, budget)
                : OutputBudget.MiddleTruncate(headText, tailText, keptCharacters, budget);
            return text.TrimEnd('\n');
        }
    }

    // The line being assembled from one stream.
    private sealed class LineState
    {
        public StringBuilder Text { get; } = new();

        public int Cut { get; set; }
    }

    private void Accept(LineState state, char c)
    {
        if (c == '\n')
        {
            EndLine(state);
        }
        else if (state.Text.Length < limit)
        {
            state.Text.Append(c);
        }
        else
        {
            state.Cut++;
        }
    }

    private void EndLine(LineState state)
    {
        if (state.Text.Length > 0 && state.Text[^1] == '\r')
        {
            state.Text.Length--;
        }

        string text = TerminalEscape().Replace(state.Text.ToString(), string.Empty);
        if (state.Cut > 0)
        {
            text += $" [... {state.Cut} characters cut from this line ...]";
        }

        state.Text.Clear();
        state.Cut = 0;
        totalLines++;

        if (filters.Count > 0 && !filters.Any(filter => text.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        matchedLines++;
        Keep(text + "\n");
    }

    private void Keep(string text)
    {
        keptCharacters += text.Length;
        SaveToSpill(text);
        if (!headClosed && head.Length + text.Length <= limit)
        {
            head.Append(text);
            return;
        }

        headClosed = true;
        tail.Enqueue(text);
        tailLength += text.Length;
        while (tailLength > limit && tail.Count > 1)
        {
            string dropped = tail.Dequeue();
            tailLength -= dropped.Length;
            omittedCharacters += dropped.Length;
        }
    }

    // Once the kept text nears the limit, everything kept so far is in head and tail (nothing has been dropped yet), so the
    // file starts with it and then follows every kept line.
    private void SaveToSpill(string text)
    {
        if (spill is null)
        {
            return;
        }

        if (spill.IsOpen)
        {
            spill.Append(text);
        }
        else if (spill.Error is null && keptCharacters > spillThreshold && omittedCharacters == 0)
        {
            spill.Start(head.ToString() + string.Concat(tail) + text);
        }
    }
}
