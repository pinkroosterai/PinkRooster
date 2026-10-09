using Spectre.Console.Testing;

namespace PinkRooster.SpectreConsole.Tests;

public sealed class AnsiConsoleAnswerExtensionsTests
{
    private const string Answer = "# Title\n\nSome **bold** and a [link](https://example.com/x) here.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```csharp\nvar x = 1;\n```";

    private static string Normalize(string output) => output.Replace("\r\n", "\n");

    [Fact]
    public void WriteAnswer_OnAnAnsiTerminal_DrawsTheMarkdown_WithoutLiteralMarkers()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();

        console.WriteAnswer(Answer);

        string output = Normalize(console.Output);
        Assert.DoesNotContain("**", output);
        Assert.DoesNotContain("|---|", output);
        Assert.Contains("Title", output);
        Assert.Contains("var", output);
        Assert.EndsWith("\n", output);
    }

    [Fact]
    public void WriteAnswer_KeepsALinksTextAndAddress()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();

        console.WriteAnswer("See [the docs](https://example.com/x) now.");

        Assert.Contains("the docs (https://example.com/x)", Normalize(console.Output));
    }

    [Fact]
    public void WriteAnswer_OnATerminalThatCannotRedraw_WritesThePlainAnswer()
    {
        TestConsole console = new();

        console.WriteAnswer("Some **bold** text with [a link](https://example.com).\n\n");

        Assert.Equal("Some **bold** text with [a link](https://example.com).\n", Normalize(console.Output));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void WriteAnswer_WithNothingToWrite_WritesNothing(string? answer)
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();

        console.WriteAnswer(answer);

        Assert.Equal("", console.Output);
    }

    [Theory]
    [InlineData("**unclosed bold and `code")]
    [InlineData("| a | b\n|--\n| 1")]
    [InlineData("```csharp\nvar x = \"unterminated")]
    [InlineData("[link](")]
    [InlineData("# \n## \n###### x\n####### y")]
    [InlineData("- a\n  - b\n    - c\n1. x\n   2. y")]
    [InlineData("> quote\n> > nested\n---\n***\n![img](u)")]
    [InlineData("<html><b>x</b></html> &amp; \u0000 \ud83d")]
    [InlineData("[[not markup]] [red]x[/] [/]")]
    public void WriteAnswer_WithMalformedMarkdown_DrawsWithoutAnError(string markdown)
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();

        console.WriteAnswer(markdown);

        Assert.NotEmpty(console.Output);
    }

    [Fact]
    public void Prepare_LeavesFencedCodeAlone()
    {
        string prepared = AnswerMarkdown.Prepare("# Title\n```\n# not a heading\n[x](y)\n---\n```\n[a](b)", ruleWidth: 5);

        Assert.Equal("## Title\n```\n# not a heading\n[x](y)\n---\n```\na (b)", prepared);
    }

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("  ----------\r")]
    public void Prepare_DrawsAHorizontalRuleItself_AtTheWidthItIsGiven(string rule)
    {
        Assert.Equal("above\n─────\nbelow", AnswerMarkdown.Prepare($"above\n{rule}\nbelow", ruleWidth: 5));
    }

    [Theory]
    [InlineData("--")]
    [InlineData("--- x")]
    [InlineData("-*-")]
    [InlineData("- - -")]
    [InlineData("___")]
    public void Prepare_LeavesALineThatIsNoRuleAlone(string line)
    {
        Assert.Equal(line, AnswerMarkdown.Prepare(line, ruleWidth: 5));
    }

    // The renderer would draw a rule as wide as System.Console.WindowWidth, which throws on Windows in a process without a console.
    [Fact]
    public void WriteAnswer_DrawsAHorizontalRule_AsWideAsTheConsoleItDrawsOn()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive().Width(20);

        console.WriteAnswer("above\n\n---\n\nbelow");

        Assert.Equal(20, console.Output.Count(character => character == '─'));
    }
}
