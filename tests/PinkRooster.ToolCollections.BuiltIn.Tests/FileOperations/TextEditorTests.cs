using PinkRooster.ToolCollections.BuiltIn.FileOperations;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.FileOperations;

public sealed class TextEditorTests
{
    [Fact]
    public void Replace_OneMatch_ReplacesItAndReportsTheLines()
    {
        EditResult result = TextEditor.Replace("a\nb\nc", "b", "B1\nB2");

        Assert.Equal("a\nB1\nB2\nc", result.Content);
        Assert.Equal([2], result.MatchLines);
        Assert.Equal((2, 2, 3), (result.StartLine, result.EndLine, result.NewEndLine));
    }

    [Fact]
    public void Replace_NoMatch_ChangesNothing()
    {
        EditResult result = TextEditor.Replace("a\nb", "x", "y");

        Assert.Null(result.Content);
        Assert.Empty(result.MatchLines);
    }

    [Fact]
    public void Replace_SeveralMatches_ChangesNothingAndNamesEachLine()
    {
        EditResult result = TextEditor.Replace("m\nx\nm\ny\nm", "m", "n");

        Assert.Null(result.Content);
        Assert.Equal([1, 3, 5], result.MatchLines);
    }

    [Fact]
    public void Replace_TargetWithLfInACrlfFile_MatchesAndWritesCrlf()
    {
        EditResult result = TextEditor.Replace("one\r\ntwo\r\nthree\r\n", "two\nthree", "2\n3");

        Assert.Equal("one\r\n2\r\n3\r\n", result.Content);
    }

    [Fact]
    public void Replace_MixedLineEndings_LeavesTheLinesOutsideTheMatchAsTheyWere()
    {
        EditResult result = TextEditor.Replace("one\ntwo\r\nthree\nfour\n", "two\nthree", "2\n3");

        Assert.Equal("one\n2\r\n3\nfour\n", result.Content);
    }

    [Fact]
    public void Replace_TargetEndingInALineBreak_CountsOnlyTheLinesItCovers()
    {
        EditResult result = TextEditor.Replace("a\nb\nc\n", "b\n", "b1\nb2\n");

        Assert.Equal((2, 2, 3), (result.StartLine, result.EndLine, result.NewEndLine));
    }

    [Fact]
    public void Replace_SeveralMatchesWithReplaceAll_ReplacesEachAndKeepsTheirLineEndings()
    {
        EditResult result = TextEditor.Replace("m\r\nx\nm\r\ny\r\nm", "m", "n", replaceAll: true);

        Assert.Equal("n\r\nx\nn\r\ny\r\nn", result.Content);
        Assert.Equal([1, 3, 5], result.MatchLines);
    }

    [Fact]
    public void NearestLines_DifferentIndentation_FindsTheLine()
    {
        var lines = TextEditor.NearestLines("a\n        return value;\nb", "return   value;");

        Assert.Equal([new NearbyLine(2, "        return value;")], lines);
    }

    [Fact]
    public void NearestLines_NothingAlike_ReturnsNone()
    {
        Assert.Empty(TextEditor.NearestLines("alpha\nbeta\ngamma", "zzzzzzzz"));
    }

    [Fact]
    public void NearestLines_ManyAlike_ReturnsTheBestThreeBestFirst()
    {
        var lines = TextEditor.NearestLines("foo(a)\nfoo(b, c)\nfoo(a, b)\nfoo(a, b, c)\nbar", "foo(a, b, c)");

        Assert.Equal(3, lines.Count);
        Assert.Equal(4, lines[0].Number);
    }
}
