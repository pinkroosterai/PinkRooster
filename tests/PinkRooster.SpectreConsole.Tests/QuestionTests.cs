using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;
using Spectre.Console.Testing;

namespace PinkRooster.SpectreConsole.Tests;

public sealed class QuestionTests
{
    private static readonly UserQuestion Budget = new("What is your budget?", "Budget",
        [new("Low", "Under 500 euro."), new("High", "Over 500 euro.")]);

    private static readonly UserQuestion Topics = new("Which topics interest you?", "Topics",
        [new("Food", "Local food."), new("Art", "Museums."), new("Nature", "Hiking.")], MultiSelect: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SingleChoice_ReturnsThePickedLabel()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        AgentConsole terminal = new(console);

        IReadOnlyList<UserQuestionAnswer> answers = await terminal.AskAsync([Budget], Token);

        UserQuestionAnswer answer = Assert.Single(answers);
        Assert.Equal(["High"], answer.SelectedLabels);
        Assert.Null(answer.OtherText);
        Assert.Contains("What is your budget? Budget", console.Output);
        Assert.Contains("You picked: High", console.Output);
    }

    [Fact]
    public async Task MultiSelect_ReturnsEveryPickedLabel()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        AgentConsole terminal = new(console);

        IReadOnlyList<UserQuestionAnswer> answers = await terminal.AskAsync([Topics], Token);

        Assert.Equal(["Food", "Art"], Assert.Single(answers).SelectedLabels);
    }

    [Fact]
    public async Task Other_AsksForTheUsersOwnAnswer()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("About 300 euro");
        AgentConsole terminal = new(console);

        IReadOnlyList<UserQuestionAnswer> answers = await terminal.AskAsync([Budget], Token);

        UserQuestionAnswer answer = Assert.Single(answers);
        Assert.Empty(answer.SelectedLabels);
        Assert.Equal("About 300 euro", answer.OtherText);
    }

    [Fact]
    public async Task SeveralQuestions_AreAskedInOrderAndNumbered()
    {
        TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        AgentConsole terminal = new(console);

        IReadOnlyList<UserQuestionAnswer> answers = await terminal.AskAsync([Budget, Topics], Token);

        Assert.Equal(["Low"], answers[0].SelectedLabels);
        Assert.Equal(["Nature"], answers[1].SelectedLabels);
        Assert.Contains("1 of 2 What is your budget?", console.Output);
        Assert.Contains("2 of 2 Which topics interest you?", console.Output);
    }

    [Fact]
    public async Task WithoutAnInteractiveTerminal_AskingThrows()
    {
        AgentConsole terminal = new(new TestConsole());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => terminal.AskAsync([Budget], Token));

        Assert.Contains("interactive terminal", error.Message);
    }
}
