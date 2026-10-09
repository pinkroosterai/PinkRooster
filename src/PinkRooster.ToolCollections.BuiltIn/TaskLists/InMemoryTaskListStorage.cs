
namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>Keeps the task list in memory only; it is gone when the process ends. The default for tests and short-lived agents.</summary>
public sealed class InMemoryTaskListStorage : ITaskListStorage
{
    private TaskListSnapshot? stored;

    /// <summary>How many times the list was read.</summary>
    internal int LoadCount { get; private set; }
    /// <summary>How many times the list was saved.</summary>
    internal int SaveCount { get; private set; }

    /// <inheritdoc />
    public ValueTask<TaskListSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        LoadCount++;
        return ValueTask.FromResult(stored?.Clone());
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(TaskListSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SaveCount++;
        stored = snapshot.Clone();
        return ValueTask.CompletedTask;
    }
}
