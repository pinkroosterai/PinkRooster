using System.Text;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

public sealed class AtomicFileWriteTests : IDisposable
{
    private readonly TempDirectory temp = new("atomic-write-");

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task CancellationDuringEncoding_LeavesTheOriginalAndRemovesTheDraft()
    {
        string path = Path.Combine(temp.Path, "file.txt");
        File.WriteAllText(path, "original");
        using CancellationTokenSource cancelled = new();
        Encoding encoding = new CancellingEncoding(cancelled.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TextFile.ReplaceAsync(path, new string('x', 8192), encoding, cancelled.Token));

        Assert.True(cancelled.IsCancellationRequested);
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal(["file.txt"], Directory.GetFiles(temp.Path).Select(Path.GetFileName));
        await TextFile.ReplaceAsync(path, "retry", TextFile.Utf8, TestContext.Current.CancellationToken);
        Assert.Equal("retry", File.ReadAllText(path));
    }

    [Fact]
    public async Task EncodingFailureAfterWritingBegins_LeavesTheOriginalAndRemovesTheDraft()
    {
        string path = Path.Combine(temp.Path, "file.txt");
        File.WriteAllText(path, "original");

        await Assert.ThrowsAsync<EncoderFallbackException>(() => TextFile.ReplaceAsync(path,
            new string('x', 8192) + "\ud800", new UTF8Encoding(false, true), TestContext.Current.CancellationToken));

        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal(["file.txt"], Directory.GetFiles(temp.Path).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileTools_KeepTheBomAndUnixPermissionsWhenReplacing(bool edit)
    {
        string path = Path.Combine(temp.Path, "file.txt");
        File.WriteAllText(path, "original\r\n", new UTF8Encoding(true));
        UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
        FileOperationsToolCollection files = new(new Workspace(temp.Path));

        string result = edit
            ? await files.EditFile("file.txt", "original", "new", cancellationToken: TestContext.Current.CancellationToken)
            : await files.WriteFile("file.txt", "new\r\n", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Error:", result);
        Assert.Equal(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("new\r\n")), File.ReadAllBytes(path));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(mode, File.GetUnixFileMode(path));
        }
        Assert.Equal(["file.txt"], Directory.GetFiles(temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AReadOnlyFile_IsLeftUnchanged()
    {
        string path = Path.Combine(temp.Path, "file.txt");
        File.WriteAllText(path, "original");
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            FileOperationsToolCollection files = new(new Workspace(temp.Path));
            string result = await files.WriteFile("file.txt", "replacement", TestContext.Current.CancellationToken);
            Assert.StartsWith("Error:", result);
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    private sealed class CancellingEncoding(Action cancel) : UTF8Encoding(false)
    {
        public override Encoder GetEncoder() => new CancellingEncoder(base.GetEncoder(), cancel);
    }

    private sealed class CancellingEncoder(Encoder inner, Action cancel) : Encoder
    {
        public override int GetByteCount(char[] chars, int index, int count, bool flush) => inner.GetByteCount(chars, index, count, flush);

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, bool flush)
        {
            int written = inner.GetBytes(chars, charIndex, charCount, bytes, byteIndex, flush);
            cancel();
            return written;
        }

        public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush)
        {
            int written = inner.GetBytes(chars, bytes, flush);
            cancel();
            return written;
        }

        public override void Convert(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
        {
            inner.Convert(chars, bytes, flush, out charsUsed, out bytesUsed, out completed);
            cancel();
        }
    }
}
