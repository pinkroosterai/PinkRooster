namespace PinkRooster.Agents.Shared;

/// <summary>Shortens text a model will read to a character limit.</summary>
internal static class TextCut
{
    /// <summary>The start and the end around a count of what was left out, never longer than <paramref name="limit"/>.</summary>
    public static string InTheMiddle(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        // Sized for the whole length: the omitted count can only be smaller.
        int available = limit - Marker(text.Length).Length;
        if (available <= 0)
        {
            return text[..limit];
        }
        int head = available / 2;
        int tail = available - head;
        return text[..head] + Marker(text.Length - head - tail) + text[^tail..];
    }

    private static string Marker(int omitted) => $"\n[... {omitted} characters omitted ...]\n";
}
