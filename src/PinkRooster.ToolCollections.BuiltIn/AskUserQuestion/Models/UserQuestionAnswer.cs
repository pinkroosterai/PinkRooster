namespace PinkRooster.ToolCollections.BuiltIn.AskUserQuestion;

/// <summary>
/// The user's answer to one <see cref="UserQuestion"/>, returned by the integrator's callback.
/// </summary>
/// <param name="SelectedLabels">The labels of the picked options, empty when none was picked.</param>
/// <param name="OtherText">The user's own answer, or <see langword="null"/> when they gave none.</param>
public sealed record UserQuestionAnswer(IReadOnlyList<string> SelectedLabels, string? OtherText = null);
