
namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>
/// Where a <see cref="TaskListToolCollection"/> keeps its one task list. An instance stores exactly one list; give each
/// session or conversation its own storage and collection.
/// </summary>
/// <remarks>
/// The collection is the only writer and serializes its own calls, so an implementation needs no locking of its own against it.
/// The collection reads once and caches the list in memory, then saves after every change. An implementation shared by several
/// processes has to guard against concurrent writers itself.
/// </remarks>
public interface ITaskListStorage
{
    /// <summary>Reads the stored list, or returns null when nothing is stored yet.</summary>
    ValueTask<TaskListSnapshot?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Stores the list, replacing what was there. The collection keeps using the snapshot afterwards, so an implementation must not hold on to it or change it.</summary>
    ValueTask SaveAsync(TaskListSnapshot snapshot, CancellationToken cancellationToken);
}
