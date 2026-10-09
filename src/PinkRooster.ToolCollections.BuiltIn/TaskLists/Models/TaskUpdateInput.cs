using System.ComponentModel;

namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>A change the model makes to one task with the <c>TaskUpdate</c> tool.</summary>
/// <param name="Id">The task to change.</param>
/// <param name="Status">The new status, or null to keep it.</param>
/// <param name="Subject">A new title, or null to keep it.</param>
/// <param name="Description">New detail, an empty string to clear it, or null to keep it.</param>
/// <param name="AddBlockedBy">Ids of tasks that must now be completed first.</param>
/// <param name="RemoveBlockedBy">Ids of tasks that no longer block this one.</param>
public sealed record TaskUpdateInput(
    [property: Description("The id of the task to change, as shown in the task list.")] int Id,
    [property: Description("The new status: pending, in_progress or completed. Leave it out to keep the current one.")] TaskItemStatus? Status = null,
    [property: Description("A new subject. Leave it out to keep the current one.")] string? Subject = null,
    [property: Description("New detail text; an empty string clears it. Leave it out to keep the current one.")] string? Description = null,
    [property: Description("Ids of tasks that must now be completed before this one.")] int[]? AddBlockedBy = null,
    [property: Description("Ids of tasks that no longer block this one.")] int[]? RemoveBlockedBy = null);
