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
