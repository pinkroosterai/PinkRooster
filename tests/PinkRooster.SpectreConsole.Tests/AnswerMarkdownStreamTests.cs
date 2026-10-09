namespace PinkRooster.SpectreConsole.Tests;

public sealed class AnswerMarkdownStreamTests
{
    private const int RuleWidth = 12;

    public static TheoryData<string> Answers =>
    [
        "# Title\n\nSome **bold** text.",
        "# Title with [a link](http://x.y/z) in it\nnext line",
        "See [the docs](http://x.y) and [more](http://a.b/c) now.\n",
        "An image ![alt](http://i.png) and a [link](u).",
        "Array a[0] is [not a link] and [neither](this one) nor [] or [x]( ) or [x]().",
        "![a](b)[c](d) and ![[e](f)",
        "Nested [a [b](u) c](v) and [a](u",
        "```cs\n# not a heading\nvar x = [a](b);\n```\n# Real heading\n[a](b)\n",
        "   ```\n[a](b)\n   ```\n[a](b)",
        "~~~\n# no\n~~~\n# yes",
        "``not a fence [a](b)\n`\n#\n##\n# \n#x",
        "Ends with a bang!",
        "Ends with [",
        "Ends with [a](",
        "Line one\r\nLine [two](http://x)\r\n# Three\r\n",
        "Above\n---\nbelow",
        "***\n",
        "  -----\r\n- item\n-- not a rule\n**bold**\n--- neither\n-",
        "```\n---\n```\n---",
        "",
        "\n\n",
    ];

    [Theory]
    [MemberData(nameof(Answers))]
    public void StreamedInPiecesOfAnySize_EqualsPrepareOfTheWhole(string answer)
    {
        string expected = AnswerMarkdown.Prepare(answer, RuleWidth);

        for (int size = 1; size <= answer.Length + 1; size++)
        {
            AnswerMarkdownStream stream = new(RuleWidth);
            string streamed = "";
            for (int index = 0; index < answer.Length; index += size)
            {
                streamed += stream.Push(answer.Substring(index, Math.Min(size, answer.Length - index)));
            }

            Assert.Equal(expected, streamed + stream.Complete());
        }
    }

    [Fact]
    public void PlainText_IsPassedOnAsItArrives()
    {
        AnswerMarkdownStream stream = new(RuleWidth);

        Assert.Equal("Some words ", stream.Push("Some words "));
        Assert.Equal("and more", stream.Push("and more"));
    }

    [Fact]
    public void AnUnfinishedLink_IsHeldBack_UntilItsClosingParenthesis()
    {
        AnswerMarkdownStream stream = new(RuleWidth);

        Assert.Equal("See ", stream.Push("See [the docs]("));
        Assert.Equal("the docs (http://x.y) now", stream.Push("http://x.y) now"));
    }

    [Fact]
    public void AHeading_IsHeldBack_UntilItsLineEnds()
    {
        AnswerMarkdownStream stream = new(RuleWidth);

        Assert.Equal("", stream.Push("# Ti"));
        Assert.Equal("## Title\n", stream.Push("tle\n"));
    }

    [Fact]
    public void ALineOfDashes_IsHeldBack_UntilItIsARuleOrSomethingElse()
    {
        AnswerMarkdownStream stream = new(RuleWidth);

        Assert.Equal("", stream.Push("--"));
        Assert.Equal("", stream.Push("-"));
        Assert.Equal("────────────\n", stream.Push("\n"));
        Assert.Equal("", stream.Push("-"));
        Assert.Equal("- item", stream.Push(" item"));
    }
}
