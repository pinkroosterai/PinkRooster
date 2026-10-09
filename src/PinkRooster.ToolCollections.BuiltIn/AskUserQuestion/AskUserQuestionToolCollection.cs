using System.ComponentModel;

namespace PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;

/// <summary>
/// Exposes an <c>AskUserQuestion</c> tool that lets an agent ask the user multiple-choice questions mid-turn.
/// The collection does not render anything: the integrator's callback shows the questions and returns the answers.
/// </summary>
/// <remarks>
/// The callback holds the tool call, and so the agent run, open until it returns. That suits hosts that can wait, such as a
/// console or desktop app; a web or server host cannot hold a request open that long. An end-turn mode for those hosts is
/// planned: the tool would return at once and end the turn, and the answers would arrive as the next user message.
/// </remarks>
/// <param name="askUser">
/// Shows the questions to the user and returns one answer per question, in the same order.
/// It receives every question of one call at once, so it decides whether to show them together or one by one.
/// </param>
public sealed class AskUserQuestionToolCollection(
    Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<IReadOnlyList<UserQuestionAnswer>>> askUser) : ToolCollection
{
    /// <summary>The most questions one call may ask.</summary>
    public const int MaxQuestions = 4;
    /// <summary>The fewest options a question may have.</summary>
    public const int MinOptions = 2;
    /// <summary>The most options a question may have.</summary>
    public const int MaxOptions = 4;
    /// <summary>The longest a question's header may be, in characters.</summary>
    public const int MaxHeaderLength = 12;

    private readonly Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<IReadOnlyList<UserQuestionAnswer>>> askUser =
        askUser ?? throw new ArgumentNullException(nameof(askUser));

    /// <summary>The <c>AskUserQuestion</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("AskUserQuestion",
        "Asks the user 1 to 4 multiple-choice questions and waits for the answers. Use it when the request is ambiguous and a wrong " +
        "guess would be costly to undo, or when the user must choose between approaches; not for facts you can look up or choices where " +
        "any sensible default will do. Each question has 2 to 4 distinct choices. Returns 'User answered: \"<question>\" = \"<labels>\"' " +
        "for each question; typed text follows as 'Other: <text>', and '(no answer)' means they skipped it.",
        Kind = ToolKind.State)]
    public async Task<string> AskUserQuestion(
        [Description("The questions to ask, 1 to 4, each with a distinct question text. Ask everything you need in one call.")] UserQuestion[] questions,
        CancellationToken cancellationToken = default)
    {
        string? error = Validate(questions);
        if (error is not null)
        {
            return error;
        }

        IReadOnlyList<UserQuestionAnswer> answers = await askUser(questions, cancellationToken).ConfigureAwait(false);
        if (answers is null || answers.Count != questions.Length)
        {
            return $"Error: The answers could not be read (got {answers?.Count ?? 0} for {questions.Length} questions). " +
                   "Do not guess them; tell the user the question could not be asked.";
        }

        IEnumerable<string> pairs = questions.Zip(answers, (question, answer) => $"\"{question.Question}\" = \"{FormatAnswer(answer)}\"");
        return $"User answered: {string.Join("; ", pairs)}. Continue with these answers.";
    }

    private static string? Validate(UserQuestion[]? questions)
    {
        if (questions is null || questions.Length is 0 or > MaxQuestions)
        {
            return $"Error: Ask between 1 and {MaxQuestions} questions; got {questions?.Length ?? 0}.";
        }

        HashSet<string> questionTexts = new(StringComparer.Ordinal);
        foreach (UserQuestion question in questions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.Question))
            {
                return "Error: Every question needs question text.";
            }

            if (!questionTexts.Add(question.Question))
            {
                return $"Error: The question \"{question.Question}\" appears more than once; each question must be unique.";
            }

            if (string.IsNullOrWhiteSpace(question.Header) || question.Header.Length > MaxHeaderLength)
            {
                return $"Error: The header of \"{question.Question}\" must be 1 to {MaxHeaderLength} characters; got \"{question.Header}\".";
            }

            int optionCount = question.Options?.Count ?? 0;
            if (optionCount is < MinOptions or > MaxOptions)
            {
                return $"Error: The question \"{question.Question}\" needs {MinOptions} to {MaxOptions} options; got {optionCount}.";
            }

            HashSet<string> labels = new(StringComparer.Ordinal);
            // The count check above returned for null options.
            foreach (UserQuestionOption option in question.Options!)
            {
                if (option is null || string.IsNullOrWhiteSpace(option.Label))
                {
                    return $"Error: Every option of \"{question.Question}\" needs a label.";
                }

                if (!labels.Add(option.Label))
                {
                    return $"Error: The option \"{option.Label}\" appears more than once in \"{question.Question}\"; labels must be unique.";
                }
            }
        }

        return null;
    }

    private static string FormatAnswer(UserQuestionAnswer? answer)
    {
        List<string> parts = [.. answer?.SelectedLabels ?? []];
        if (!string.IsNullOrWhiteSpace(answer?.OtherText))
        {
            parts.Add($"Other: {answer.OtherText}");
        }

        return parts.Count == 0 ? "(no answer)" : string.Join(", ", parts);
    }
}
