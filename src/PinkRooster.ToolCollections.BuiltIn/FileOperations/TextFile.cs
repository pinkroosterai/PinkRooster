using System.Text;

namespace PinkRooster.ToolCollections.BuiltIn.FileOperations;

/// <summary>How the file tools read and write text: UTF-8, a byte order mark kept only where the file had one, and binary content refused.</summary>
internal static class TextFile
{
    /// <summary>UTF-8 without a byte order mark: how new files are written.</summary>
    public static readonly Encoding Utf8 = new UTF8Encoding(false);

    /// <summary>UTF-8 with a byte order mark: how a file that had one is written back.</summary>
    public static readonly Encoding Utf8WithBom = new UTF8Encoding(true);

    /// <summary>Reading: invalid bytes are an error instead of replacement characters.</summary>
    public static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    /// <summary>
    /// Encodes the whole replacement first, then rewrites the existing file in place, so the file keeps what it is: its owner,
    /// permissions, hard links and access rules. A file that cannot be opened for writing is refused by the system.
    /// </summary>
    /// <remarks>
    /// Cancellation and encoding failures happen before the file is opened and leave it unchanged. Once writing starts it is not
    /// cancelled, so a cancellation never leaves the file half written.
    /// </remarks>
    public static async Task ReplaceAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken)
    {
        byte[] bytes = Encode(content, encoding, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        FileStreamOptions options = new() { Mode = FileMode.Open, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
        await using FileStream stream = new(path, options);
        // Writing over the old bytes before cutting the rest leaves no moment where the file is empty.
        await stream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
        stream.SetLength(bytes.Length);
        await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    // The byte order mark the encoding asks for, then the text, a block at a time so a cancellation is seen during a long encoding.
    private static byte[] Encode(string content, Encoding encoding, CancellationToken cancellationToken)
    {
        const int BlockCharacters = 4096;
        Encoder encoder = encoding.GetEncoder();
        byte[] block = new byte[encoding.GetMaxByteCount(BlockCharacters)];
        using MemoryStream buffer = new();
        buffer.Write(encoding.GetPreamble());
        ReadOnlySpan<char> rest = content;
        bool completed;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            int take = Math.Min(rest.Length, BlockCharacters);
            encoder.Convert(rest[..take], block, flush: take == rest.Length, out int used, out int written, out completed);
            buffer.Write(block, 0, written);
            rest = rest[used..];
            cancellationToken.ThrowIfCancellationRequested();
        }
        while (rest.Length > 0 || !completed);
        return buffer.ToArray();
    }

    /// <summary>True when a NUL byte is in the first block, which marks a binary file before any of it is decoded.</summary>
    public static bool StartsWithBinaryContent(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        Span<byte> buffer = stackalloc byte[4096];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return buffer[..read].Contains((byte)0);
    }

    /// <summary>The encoding to write a file back in: with a byte order mark when it had one.</summary>
    public static Encoding EncodingToWrite(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        Span<byte> buffer = stackalloc byte[3];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return buffer[..read].SequenceEqual(Encoding.UTF8.Preamble) ? Utf8WithBom : Utf8;
    }

    /// <summary>Decodes a whole file; false for binary content or bytes that are not valid UTF-8. The byte order mark is not part of the text.</summary>
    public static bool TryDecode(byte[] bytes, out string text, out bool hasByteOrderMark)
    {
        text = string.Empty;
        hasByteOrderMark = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        if (bytes.AsSpan().Contains((byte)0))
        {
            return false;
        }

        int skip = hasByteOrderMark ? Encoding.UTF8.Preamble.Length : 0;
        try
        {
            text = StrictUtf8.GetString(bytes, skip, bytes.Length - skip);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
