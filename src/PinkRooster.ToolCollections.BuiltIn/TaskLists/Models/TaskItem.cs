namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>One task of a <see cref="TaskListSnapshot"/>.</summary>
public sealed class TaskItem
{
    /// <summary>The task's number, unique in its list and never reused after the task is removed.</summary>
    public int Id { get; set; }
    /// <summary>A short, action-oriented title.</summary>
    public string Subject { get; set; } = string.Empty;
    /// <summary>Optional detail, shown only by the <c>TaskGet</c> tool.</summary>
    public string? Description { get; set; }
    /// <summary>Where the task stands.</summary>
    public TaskItemStatus Status { get; set; }
    /// <summary>The ids of tasks that must be completed before this one can start.</summary>
    public List<int> BlockedBy { get; set; } = [];

    /// <summary>Returns an independent copy.</summary>
    public TaskItem Clone() => new()
    {
        Id = Id,
        Subject = Subject,
        Description = Description,
        Status = Status,
        BlockedBy = [.. BlockedBy]
    };
}
