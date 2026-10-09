using System.ComponentModel;

namespace PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;

/// <summary>
/// One choice the user can pick for a <see cref="UserQuestion"/>.
/// </summary>
/// <param name="Label">The short text the user picks.</param>
/// <param name="Description">What the choice means or what happens when it is picked.</param>
public sealed record UserQuestionOption(
    [property: Description("The short text the user picks, 1 to 5 words.")] string Label,
    [property: Description("One line on what this choice means or what happens if the user picks it, including any trade-off.")] string Description);
