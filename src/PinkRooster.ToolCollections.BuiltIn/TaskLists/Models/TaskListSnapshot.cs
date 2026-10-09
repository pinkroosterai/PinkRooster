namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>The whole stored state of one task list.</summary>
public sealed class TaskListSnapshot
{
    /// <summary>The id the next added task gets. Kept so an id is never reused after a removal.</summary>
    public int NextId { get; set; } = 1;
    /// <summary>The tasks, in the order they were added.</summary>
    public List<TaskItem> Items { get; set; } = [];

    /// <summary>Returns an independent copy.</summary>
    public TaskListSnapshot Clone() => new() { NextId = NextId, Items = [.. Items.Select(item => item.Clone())] };
}
