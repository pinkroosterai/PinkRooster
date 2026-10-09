using System.ComponentModel;

namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>A task the model adds with the <c>TaskAdd</c> tool.</summary>
/// <param name="Subject">A short, action-oriented title.</param>
/// <param name="Description">Optional detail.</param>
/// <param name="BlockedBy">Tasks that must be completed first: an existing task id such as "3", or "@2" for the second task of this same call.</param>
public sealed record TaskAddInput(
    [property: Description("A short, action-oriented title, for example 'Write the storage tests'.")] string Subject,
    [property: Description("Optional detail that the task list view leaves out; only needed when the subject is not enough to do the task.")] string? Description = null,
    [property: Description("Optional tasks that must be completed before this one: an existing task id such as \"3\", or \"@2\" for the second task of this same call.")] string[]? BlockedBy = null);
