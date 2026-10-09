using System.Text.Json;
using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;

namespace PinkRooster.ToolCollections.BuiltIn.Tests.AskUserQuestion;

public sealed class AskUserQuestionToolCollectionTests
{
    private static readonly UserQuestion Budget = new("What is your budget?", "Budget",
        [new("Low", "Under 500 euro."), new("High", "Over 500 euro.")]);

    private static readonly UserQuestion Topics = new("Which topics interest you?", "Topics",
        [new("Food", "Local food."), new("Art", "Museums."), new("Nature", "Hiking.")], MultiSelect: true);

    [Fact]
    public async Task AskUserQuestion_PassesEveryQuestionToTheCallbackAtOnce()
    {
        IReadOnlyList<UserQuestion>? received = null;
        AskUserQuestionToolCollection collection = new((questions, _) =>
        {
            received = questions;
            return Answers(new UserQuestionAnswer(["Low"]), new(["Food"]));
        });

        await collection.AskUserQuestion([Budget, Topics]);

        Assert.Equal([Budget, Topics], received);
    }

    [Fact]
    public async Task AskUserQuestion_FormatsSingleMultiOtherAndEmptyAnswers()
    {
        UserQuestion when = new("When do you travel?", "When", [new("Spring", "March to May."), new("Summer", "June to August.")]);
        UserQuestion who = new("Who travels?", "Who", [new("Alone", "Just me."), new("Family", "With kids.")]);
        AskUserQuestionToolCollection collection = new((_, _) => Answers(
            new(["Low"]),
            new(["Food", "Art"]),
            new([], "Autumn"),
            new([])));

        string result = await collection.AskUserQuestion([Budget, Topics, when, who]);

        Assert.Equal(
            "User answered: \"What is your budget?\" = \"Low\"; \"Which topics interest you?\" = \"Food, Art\"; " +
            "\"When do you travel?\" = \"Other: Autumn\"; \"Who travels?\" = \"(no answer)\". Continue with these answers.",
            result);
    }

    [Fact]
    public async Task AskUserQuestion_CombinesSelectedLabelsWithOtherText()
    {
        AskUserQuestionToolCollection collection = new((_, _) => Answers(new UserQuestionAnswer(["Food"], "Architecture")));

        string result = await collection.AskUserQuestion([Topics]);

        Assert.Contains("\"Food, Other: Architecture\"", result);
    }

    public static TheoryData<UserQuestion[], string> InvalidCalls => new()
    {
        { [], "Ask between 1 and 4 questions; got 0." },
        { [Budget, Topics, Budget with { Question = "a?" }, Budget with { Question = "b?" }, Budget with { Question = "c?" }], "Ask between 1 and 4 questions; got 5." },
        { [Budget with { Question = " " }], "Every question needs question text." },
        { [Budget, Budget], "appears more than once; each question must be unique." },
        { [Budget with { Header = "" }], "must be 1 to 12 characters" },
        { [Budget with { Header = "Thirteen char" }], "must be 1 to 12 characters" },
        { [Budget with { Options = [new("Low", "Cheap.")] }], "needs 2 to 4 options; got 1." },
        { [Budget with { Options = [new("A", ""), new("B", ""), new("C", ""), new("D", ""), new("E", "")] }], "needs 2 to 4 options; got 5." },
        { [Budget with { Options = [new("Low", ""), new(" ", "")] }], "needs a label." },
        { [Budget with { Options = [new("Low", ""), new("Low", "")] }], "labels must be unique." },
    };

    [Theory]
    [MemberData(nameof(InvalidCalls))]
    public async Task AskUserQuestion_InvalidCall_ReturnsErrorWithoutCallingTheCallback(UserQuestion[] questions, string expectedError)
    {
        bool called = false;
        AskUserQuestionToolCollection collection = new((_, _) =>
        {
            called = true;
            return Answers();
        });

        string result = await collection.AskUserQuestion(questions);

        Assert.Contains(expectedError, result);
        Assert.False(called);
    }

    [Fact]
    public async Task AskUserQuestion_WrongAnswerCount_ReturnsError()
    {
        AskUserQuestionToolCollection collection = new((_, _) => Answers(new UserQuestionAnswer(["Low"])));

        string result = await collection.AskUserQuestion([Budget, Topics]);

        Assert.Equal("Error: The answers could not be read (got 1 for 2 questions). Do not guess them; tell the user the question could not be asked.", result);
    }

    [Fact]
    public async Task AskUserQuestion_NullAnswerEntry_IsReportedAsNoAnswer()
    {
        AskUserQuestionToolCollection collection = new((_, _) => Answers(new UserQuestionAnswer[] { null! }));

        string result = await collection.AskUserQuestion([Budget]);

        Assert.Equal("User answered: \"What is your budget?\" = \"(no answer)\". Continue with these answers.", result);
    }

    [Fact]
    public async Task AskUserQuestion_CallbackCancellation_Propagates()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        AskUserQuestionToolCollection collection = new(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return [];
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection.AskUserQuestion([Budget], cancellation.Token));
    }

    [Fact]
    public void Descriptions_StateTheLimitsTheCollectionEnforces()
    {
        AIFunction function = Assert.Single(new AskUserQuestionToolCollection((_, _) => Answers()).GetAIFunctions());
        string schema = function.JsonSchema.ToString();

        Assert.Contains($"1 to {AskUserQuestionToolCollection.MaxQuestions} multiple-choice questions", function.Description);
        Assert.Contains($"{AskUserQuestionToolCollection.MinOptions} to {AskUserQuestionToolCollection.MaxOptions} distinct choices", function.Description);
        Assert.Contains($"1 to {AskUserQuestionToolCollection.MaxQuestions}, each with a distinct", schema);
        Assert.Contains($"The {AskUserQuestionToolCollection.MinOptions} to {AskUserQuestionToolCollection.MaxOptions} choices", schema);
        Assert.Contains($"at most {AskUserQuestionToolCollection.MaxHeaderLength} characters", schema);
    }

    [Fact]
    public void Description_NamesTheResultFormat()
    {
        string description = Assert.Single(new AskUserQuestionToolCollection((_, _) => Answers()).GetAIFunctions()).Description;

        Assert.Contains("'Other: <text>'", description);
        Assert.Contains("'(no answer)'", description);
    }

    [Fact]
    public async Task AskUserQuestion_InvalidCall_StartsWithErrorPrefix()
    {
        string result = await new AskUserQuestionToolCollection((_, _) => Answers()).AskUserQuestion([]);

        Assert.StartsWith("Error: ", result);
    }

    [Fact]
    public void Constructor_NullCallback_Throws()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new AskUserQuestionToolCollection(null!));

        Assert.Equal("askUser", exception.ParamName);
    }

    [Fact]
    public void GetAIFunctions_ExposesOneToolWithDescribedQuestionSchema()
    {
        AskUserQuestionToolCollection collection = new((_, _) => Answers());

        AIFunction function = Assert.Single(collection.GetAIFunctions());

        Assert.Equal("AskUserQuestion", function.Name);
        string schema = function.JsonSchema.ToString();
        Assert.Contains("\"header\"", schema);
        Assert.Contains("\"options\"", schema);
        Assert.Contains("\"multiSelect\"", schema);
        Assert.Contains("at most 12 characters", schema);
        Assert.Contains("The short text the user picks", schema);
        Assert.DoesNotContain("cancellationToken", schema);
    }

    [Fact]
    public async Task GetAIFunctions_InvokedWithJsonArguments_RoundTripsThroughTheCallback()
    {
        IReadOnlyList<UserQuestion>? received = null;
        AskUserQuestionToolCollection collection = new((questions, _) =>
        {
            received = questions;
            return Answers(new UserQuestionAnswer(["Art", "Nature"]));
        });
        AIFunction function = Assert.Single(collection.GetAIFunctions());
        JsonElement questionsJson = JsonDocument.Parse("""
            [{ "question": "Which topics interest you?", "header": "Topics", "multiSelect": true,
               "options": [{ "label": "Art", "description": "Museums." }, { "label": "Nature", "description": "Hiking." }] }]
            """).RootElement;

        object? result = await function.InvokeAsync(new AIFunctionArguments { ["questions"] = questionsJson });

        UserQuestion question = Assert.Single(received!);
        Assert.True(question.MultiSelect);
        Assert.Equal(["Art", "Nature"], question.Options.Select(option => option.Label));
        Assert.Contains("\"Art, Nature\"", result?.ToString());
    }

    private static Task<IReadOnlyList<UserQuestionAnswer>> Answers(params UserQuestionAnswer[] answers) =>
        Task.FromResult<IReadOnlyList<UserQuestionAnswer>>(answers);
}
