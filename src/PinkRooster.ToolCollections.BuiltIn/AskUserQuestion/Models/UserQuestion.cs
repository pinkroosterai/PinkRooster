using System.ComponentModel;

namespace PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;

/// <summary>
/// A multiple-choice question an agent asks the user through <see cref="AskUserQuestionToolCollection"/>.
/// </summary>
/// <param name="Question">The full question text.</param>
/// <param name="Header">A short label for the question, at most <see cref="AskUserQuestionToolCollection.MaxHeaderLength"/> characters.</param>
/// <param name="Options">The choices, 2 to 4. The user can always answer in their own words as well.</param>
/// <param name="MultiSelect">Whether the user may pick more than one choice.</param>
public sealed record UserQuestion(
    [property: Description("The full question, ending with a question mark, specific enough to answer without other context, for example 'Which database should the new service use?'.")] string Question,
    [property: Description("A very short label for the question, at most 12 characters, for example 'Budget' or 'Approach'.")] string Header,
    [property: Description("The 2 to 4 choices. Do not add an 'Other' choice; the user can always answer in their own words.")] IReadOnlyList<UserQuestionOption> Options,
    [property: Description("True when the choices aren't mutually exclusive and the user may pick several; false (the default) for exactly one.")] bool MultiSelect = false);
