using Spectre.Console.Testing;

namespace PinkRooster.SpectreConsole.Tests;

public sealed class AnsiConsoleExtensionsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void CanRenderMarkdown_NeedsBothAnsiAndAnInteractiveTerminal(bool ansi, bool interactive, bool expected)
    {
        TestConsole console = new();
        console.Profile.Capabilities.Ansi = ansi;
        console.Profile.Capabilities.Interactive = interactive;

        Assert.Equal(expected, console.CanRenderMarkdown());
    }

    [Fact]
    public async Task AskOrNullAsync_ReturnsTheLineTyped()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("hello");

        string? line = await console.AskOrNullAsync("You: ", Token);

        Assert.Equal("hello", line);
        Assert.Contains("You: ", console.Output);
    }

    [Fact]
    public async Task AskOrNullAsync_WithAStyle_ColoursThePromptText()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();
        console.Input.PushTextWithEnter("x");

        await console.AskOrNullAsync("You: ", Token, "#ff5faf");

        Assert.Contains("\u001b[38;", console.Output);
    }

    [Fact]
    public async Task AskOrNullAsync_WithoutAStyle_LeavesThePromptTextPlain()
    {
        TestConsole console = new TestConsole().EmitAnsiSequences().Interactive();
        console.Input.PushTextWithEnter("x");

        await console.AskOrNullAsync("You: ", Token);

        Assert.DoesNotContain("\u001b[38;", console.Output);
    }

    [Fact]
    public async Task AskOrNullAsync_TreatsBracketsInThePromptAsText()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("x");

        await console.AskOrNullAsync("[not markup] ", Token);

        Assert.Contains("[not markup]", console.Output);
    }

    [Fact]
    public async Task AskOrNullAsync_WithoutAnInteractiveTerminal_ReturnsNull()
    {
        Assert.Null(await new TestConsole().AskOrNullAsync("You: ", Token));
    }

    [Fact]
    public async Task AskOrNullAsync_WhenCancelled_ReturnsNull()
    {
        TestConsole console = new TestConsole().Interactive();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Assert.Null(await console.AskOrNullAsync("You: ", cancelled.Token));
    }

    [Fact]
    public async Task AskOrNullAsync_WhenInputHasEnded_ReturnsNull()
    {
        TestConsole console = new TestConsole().Interactive();

        Assert.Null(await console.AskOrNullAsync("You: ", Token));
    }
}
