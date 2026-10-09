using System.Text.Json.Serialization;

namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>Where a task stands. Named <c>TaskItemStatus</c> so it does not clash with <see cref="System.Threading.Tasks.TaskStatus"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskItemStatus>))]
public enum TaskItemStatus
{
    /// <summary>Not started.</summary>
    [JsonStringEnumMemberName("pending")] Pending,
    /// <summary>Being worked on now.</summary>
    [JsonStringEnumMemberName("in_progress")] InProgress,
    /// <summary>Done.</summary>
    [JsonStringEnumMemberName("completed")] Completed
}
