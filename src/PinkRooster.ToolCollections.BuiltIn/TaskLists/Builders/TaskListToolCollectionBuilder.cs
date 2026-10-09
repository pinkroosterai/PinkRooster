using Microsoft.Extensions.Logging;

namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>Fluent builder for a <see cref="TaskListToolCollection"/>: the storage, limits and switches, then <see cref="Build"/>.</summary>
/// <remarks>
/// The builder is mutable: every method changes it and returns it. Everything is optional; without a storage the list lives in memory.
/// </remarks>
/// <example>
/// <code>
/// TaskListToolCollection tasks = new TaskListToolCollectionBuilder()
///     .WithJsonFile(".pinkrooster/tasks.json")
///     .WithMaxVisibleItems(10)
///     .Build();
/// </code>
/// </example>
public sealed class TaskListToolCollectionBuilder
{
    private ITaskListStorage? storage;
    private string? jsonFilePath;
    private ILogger? logger;
    private int maxVisibleItems = TaskListOptions.DefaultMaxVisibleItems;
    private int maxSubjectLength = TaskListOptions.DefaultMaxSubjectLength;
    private bool singleInProgress = true;
    private bool injectContext = true;
    private bool perSession;
    private Func<TaskListSnapshot, string?>? contextFormatter;

    /// <summary>Sets where the list is kept. Without it the list lives in memory.</summary>
    public TaskListToolCollectionBuilder WithStorage(ITaskListStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        this.storage = storage;
        return this;
    }

    /// <summary>Keeps the list in a JSON file, which is loaded when the collection is built and saved on every change.</summary>
    public TaskListToolCollectionBuilder WithJsonFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        jsonFilePath = path;
        storage = null;
        return this;
    }

    /// <summary>Sets the most open tasks the injected view lists. Without it the limit is 15.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxVisibleItems"/> is below 1.</exception>
    public TaskListToolCollectionBuilder WithMaxVisibleItems(int maxVisibleItems)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxVisibleItems, 1);
        this.maxVisibleItems = maxVisibleItems;
        return this;
    }

    /// <summary>Sets the longest subject the injected view shows. Without it the limit is 80.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSubjectLength"/> is below 10.</exception>
    public TaskListToolCollectionBuilder WithMaxSubjectLength(int maxSubjectLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSubjectLength, 10);
        this.maxSubjectLength = maxSubjectLength;
        return this;
    }

    /// <summary>
    /// Gives each session its own list, kept in the session, so one collection serves every conversation of an agent and a list travels
    /// with its session when that is saved and restored. It cannot be combined with <see cref="WithStorage"/> or <see cref="WithJsonFile"/>.
    /// </summary>
    public TaskListToolCollectionBuilder PerSession()
    {
        perSession = true;
        return this;
    }

    /// <summary>Lets more than one task be in progress at once.</summary>
    public TaskListToolCollectionBuilder AllowMultipleInProgress()
    {
        singleInProgress = false;
        return this;
    }

    /// <summary>Stops the list from being sent before every model call; the model reads it with <c>TaskGet</c>.</summary>
    public TaskListToolCollectionBuilder WithoutContext()
    {
        injectContext = false;
        return this;
    }

    /// <summary>Replaces the built-in view of the list. The formatter gets a copy of the list and returns the text to inject, or null for nothing.</summary>
    public TaskListToolCollectionBuilder FormatContextWith(Func<TaskListSnapshot, string?> formatter)
    {
        contextFormatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        return this;
    }

    /// <summary>Sets the logger the collection and a JSON file storage write problems to.</summary>
    public TaskListToolCollectionBuilder WithLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
        return this;
    }

    /// <summary>Builds a new collection from the builder's current settings. Can be called more than once; a JSON file or memory storage is created for each, a storage given with <see cref="WithStorage"/> is shared.</summary>
    /// <exception cref="InvalidOperationException"><see cref="PerSession"/> was combined with a storage; the message names the fix.</exception>
    public TaskListToolCollection Build()
    {
        if (perSession && (storage is not null || jsonFilePath is not null))
        {
            throw new InvalidOperationException(
                "PerSession keeps each list in its session, so a storage is never read or written. Remove PerSession(), or remove WithStorage and WithJsonFile.");
        }

        ITaskListStorage resolved = storage
            ?? (jsonFilePath is null ? new InMemoryTaskListStorage() : new JsonFileTaskListStorage(jsonFilePath, logger));
        return new TaskListToolCollection(
            resolved,
            new TaskListOptions
            {
                MaxVisibleItems = maxVisibleItems,
                MaxSubjectLength = maxSubjectLength,
                SingleInProgress = singleInProgress,
                InjectContext = injectContext,
                PerSession = perSession,
                ContextFormatter = contextFormatter,
            },
            logger);
    }
}
