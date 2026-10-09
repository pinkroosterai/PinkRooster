
namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>Limits and switches for a <see cref="TaskListToolCollection"/>; <see cref="TaskListToolCollectionBuilder"/> sets them.</summary>
internal sealed class TaskListOptions
{
    public const int DefaultMaxVisibleItems = 15;
    public const int DefaultMaxSubjectLength = 80;

    /// <summary>The most open tasks the injected view lists; the rest are counted. Open tasks are shown in-progress first, then startable, then blocked. Default 15.</summary>
    public int MaxVisibleItems { get; init; } = DefaultMaxVisibleItems;

    /// <summary>The longest subject the injected view shows, in characters; longer ones end in "...". Default 80.</summary>
    public int MaxSubjectLength { get; init; } = DefaultMaxSubjectLength;

    /// <summary>Whether at most one task may be in progress at once. Default true.</summary>
    public bool SingleInProgress { get; init; } = true;

    /// <summary>Whether the current list is sent before every model call. Default true; when off, the model reads it with <c>TaskGet</c>.</summary>
    public bool InjectContext { get; init; } = true;

    /// <summary>
    /// Whether each session has its own list, kept in the session instead of the storage, so one collection serves every conversation
    /// of an agent and a list travels with its session when that is saved and restored. Default false: one list per collection, in the storage.
    /// </summary>
    public bool PerSession { get; init; }

    /// <summary>Replaces the built-in view of the list. It gets a copy of the list and returns the text to inject, or null for nothing.</summary>
    public Func<TaskListSnapshot, string?>? ContextFormatter { get; init; }
}
