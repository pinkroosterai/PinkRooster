namespace PinkRooster.SpectreConsole;

/// <summary>Redacts sensitive values in text that arrives in pieces, holding back the end of the text while it could still be the start of one.</summary>
/// <param name="values">The sensitive values, without empty ones.</param>
/// <param name="redact">Replaces every sensitive value in a whole text.</param>
internal sealed class StreamingRedactor(IReadOnlyCollection<string> values, Func<string, string> redact)
{
    // One character fewer than the longest value: a shorter tail cannot be the start of one that is not complete yet.
    private readonly int keep = values.Count == 0 ? 0 : values.Max(value => value.Length) - 1;
    private string held = "";

    /// <summary>Takes the next piece and returns the text that is safe to draw now.</summary>
    public string Push(string text)
    {
        if (keep == 0)
        {
            return redact(text);
        }

        string raw = held + text;
        int cut = Math.Max(0, raw.Length - keep);
        // A value that starts before the cut and ends after it stays whole in the held part.
        for (int start = Math.Max(0, cut - keep); start < cut; start++)
        {
            if (values.Any(value => start + value.Length > cut && string.CompareOrdinal(raw, start, value, 0, value.Length) == 0))
            {
                cut = start;
                break;
            }
        }

        held = raw[cut..];
        return redact(raw[..cut]);
    }

    /// <summary>Returns the held text, redacted, at the end of the stream.</summary>
    public string Flush()
    {
        string rest = redact(held);
        held = "";
        return rest;
    }
}
