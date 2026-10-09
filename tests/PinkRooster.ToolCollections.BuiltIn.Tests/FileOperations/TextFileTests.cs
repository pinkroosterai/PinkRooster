using System.Text;
using PinkRooster.ToolCollections.BuiltIn.FileOperations;
using PinkRooster.ToolCollections.BuiltIn.Tests.TestSupport;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

public sealed class TextFileTests : IDisposable
{
    private readonly TempDirectory temp = new("textfile-test-");

    public void Dispose() => temp.Dispose();

    [Fact]
    public void TryDecode_Utf8WithByteOrderMark_ReturnsTheTextWithoutIt()
    {
        byte[] bytes = [.. Encoding.UTF8.Preamble, .. "héllo"u8];

        Assert.True(TextFile.TryDecode(bytes, out string text, out bool hasByteOrderMark));
        Assert.Equal("héllo", text);
        Assert.True(hasByteOrderMark);
    }

    [Theory]
    [InlineData(new byte[] { 0x63, 0x61, 0x66, 0xE9 })]
    [InlineData(new byte[] { 0x48, 0x00, 0x69 })]
    public void TryDecode_InvalidUtf8OrNulByte_ReturnsFalse(byte[] bytes)
    {
        Assert.False(TextFile.TryDecode(bytes, out string text, out _));
        Assert.Empty(text);
    }

    [Fact]
    public void StartsWithBinaryContent_NulInTheFirstBlock_ReturnsTrue()
    {
        string path = Path.Combine(temp.Path, "bin");
        File.WriteAllBytes(path, [0x41, 0x00, 0x42]);

        Assert.True(TextFile.StartsWithBinaryContent(path));
    }

    [Fact]
    public void StartsWithBinaryContent_EmptyFile_ReturnsFalse()
    {
        string path = Path.Combine(temp.Path, "empty");
        File.WriteAllBytes(path, []);

        Assert.False(TextFile.StartsWithBinaryContent(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EncodingToWrite_FollowsTheByteOrderMarkTheFileHad(bool hadByteOrderMark)
    {
        string path = Path.Combine(temp.Path, "f.txt");
        File.WriteAllText(path, "x", new UTF8Encoding(hadByteOrderMark));

        Encoding encoding = TextFile.EncodingToWrite(path);

        Assert.Equal(hadByteOrderMark, encoding.GetPreamble().Length > 0);
    }
}
