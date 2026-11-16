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

    /// <summary>Writes a replacement beside an existing file and commits it only after the complete write, keeping its permissions.</summary>
    public static async Task ReplaceAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
        {
            throw new UnauthorizedAccessException($"File '{path}' is read-only.");
        }

        string draft = Path.Combine(Path.GetDirectoryName(path)!, $".pinkrooster-{Guid.NewGuid():N}.tmp");
        FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = File.GetUnixFileMode(path);
        }
        try
        {
            await using (FileStream stream = new(draft, options))
            await using (StreamWriter writer = new(stream, encoding))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (!OperatingSystem.IsWindows())
            {
                // UnixCreateMode is filtered by umask; keep the original mode exactly, including executable bits.
                File.SetUnixFileMode(draft, options.UnixCreateMode!.Value);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // A sibling is on the same volume. Replace also keeps the destination's ACL on Windows.
            File.Replace(draft, path, destinationBackupFileName: null);
        }
        finally
        {
            File.Delete(draft);
        }
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
