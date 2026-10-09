using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace PinkRooster.ToolCollections.BuiltIn.TaskLists;

/// <summary>
/// Gives an agent a task list to plan and track multi-step work: four batched tools, and the current list sent before every
/// model call in a compact form so the model never has to ask for it.
/// </summary>
/// <remarks>
/// <para>
/// By default one instance is one list. It reads its storage once, keeps the list in memory and saves after every change, and that
/// list is shared by every agent and session the instance is given to. With <see cref="TaskListToolCollectionBuilder.PerSession"/> each session has
/// its own list instead, kept in the session and not in the storage, so one instance serves every conversation of an agent.
/// </para>
/// <para>
/// A call that changes the list is all-or-nothing: it is checked against the list as it will be after the whole call, so the
/// order of entries in a call does not matter, and a call that breaks a rule changes nothing. The rules: a task cannot be in
/// progress or completed while a task it waits on is open, tasks cannot wait on each other in a loop, and
/// only one task is in progress unless <see cref="TaskListToolCollectionBuilder.AllowMultipleInProgress"/> is set.
/// </para>
/// <para>
/// With the agent builder the list arrives at the end of each request, which keeps the start of the prompt stable for prompt
/// caching. A <see cref="Context.ToolCollectionContextProvider"/> sends it as instructions, which changes the start of the prompt
/// whenever the list changes; prefer the builder for this collection.
/// </para>
/// </remarks>
public sealed class TaskListToolCollection : ToolCollection
{
    private readonly ITaskListStorage storage;
    private readonly TaskListOptions options;
    private readonly ILogger? logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private TaskListSnapshot? current;

    /// <summary>Creates a task list kept in memory, with the default limits; it is gone when the process ends.</summary>
    /// <remarks>For a list per session, limits, the view or a logger use <see cref="TaskListToolCollectionBuilder"/>.</remarks>
    public TaskListToolCollection() : this(new InMemoryTaskListStorage())
    {
    }

    /// <summary>Creates a task list kept in <paramref name="storage"/>, with the default limits.</summary>
    /// <remarks>For a list per session, limits, the view or a logger use <see cref="TaskListToolCollectionBuilder"/>.</remarks>
    /// <param name="storage">Where the list is kept; one storage per collection.</param>
    /// <exception cref="ArgumentNullException">The storage is null.</exception>
    public TaskListToolCollection(ITaskListStorage storage) : this(storage, options: null)
    {
    }

    /// <param name="storage">Where the list is kept; one storage per collection.</param>
    /// <param name="options">Limits and switches; the defaults when null.</param>
    /// <param name="logger">Where a failing storage is logged.</param>
    /// <exception cref="ArgumentOutOfRangeException">A limit in <paramref name="options"/> is too small.</exception>
    internal TaskListToolCollection(ITaskListStorage storage, TaskListOptions? options, ILogger? logger = null)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.options = options ?? new TaskListOptions();
        this.logger = logger;
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaxVisibleItems, 1, nameof(options.MaxVisibleItems));
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaxSubjectLength, 10, nameof(options.MaxSubjectLength));

        AddInstruction("Keep a task list for work that takes 3 or more steps; skip it for simple requests.");
        AddInstruction(this.options.SingleInProgress
            ? "Add every step up front in one TaskAdd call, keep exactly one task in_progress, and mark each task completed as soon as it is done."
            : "Add every step up front in one TaskAdd call, mark a task in_progress when you start it, and mark it completed as soon as it is done.");
        AddInstruction(this.options.InjectContext
            ? "The current task list is shown to you automatically; don't list it back. Use TaskGet only for descriptions, or for tasks the view leaves out."
            : "The task list is not shown automatically; call TaskGet to read it.");
        AddConstraint("Never mark a task completed while its work is unfinished or blocked.");
    }

    /// <summary>Returns the compact view of the list, or null when the list is empty or context is switched off.</summary>
    /// <remarks>An agent sends it before every model call. It reads the in-memory list, not the storage.</remarks>
    public override async ValueTask<string?> GetContextAsync(CancellationToken cancellationToken)
    {
        if (!options.InjectContext)
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TaskListSnapshot snapshot = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return options.ContextFormatter is { } formatter ? formatter(snapshot.Clone()) : TaskListFormatter.FormatContext(snapshot, options);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The <c>TaskAdd</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("TaskAdd",
        "Adds tasks to your task list and returns their ids. Add all the steps of a job in one call instead of one call per task. " +
        "A task can wait on others through blockedBy: use an existing task id such as \"3\", or \"@2\" for the second task of this same call. " +
        "A task that waits on an open task cannot be started or completed until that one is completed. " +
        "If any entry is invalid nothing is added and it returns 'Error: <what to fix>'.",
        Kind = ToolKind.State)]
    public Task<string> TaskAdd(
        [Description("The tasks to add, in order; at least one. Each has a short subject, and optionally a description and the tasks it waits on.")] TaskAddInput[] tasks,
        CancellationToken cancellationToken = default)
    {
        if (tasks is null || tasks.Length == 0)
        {
            return Task.FromResult("Error: Add at least one task. Nothing was changed.");
        }

        return MutateAsync(work =>
        {
            int first = work.NextId;
            List<TaskItem> added = [];
            for (int i = 0; i < tasks.Length; i++)
            {
                TaskAddInput? input = tasks[i];
                if (input is null || string.IsNullOrWhiteSpace(input.Subject))
                {
                    return (Error($"Task {i + 1} needs a subject."), string.Empty);
                }

                TaskItem item = new() { Id = first + i, Subject = input.Subject.Trim(), Description = CleanDescription(input.Description) };
                foreach (string? raw in input.BlockedBy ?? [])
                {
                    if (!TryResolveBlocker(raw, first, tasks.Length, work, out int blockerId, out string? problem))
                    {
                        return (Error($"Task {i + 1}: {problem}"), string.Empty);
                    }

                    if (!item.BlockedBy.Contains(blockerId))
                    {
                        item.BlockedBy.Add(blockerId);
                    }
                }
                added.Add(item);
            }

            work.Items.AddRange(added);
            work.NextId = first + tasks.Length;
            return (null, $"Added {TaskListFormatter.Ids(added.Select(item => item.Id))}.");
        }, cancellationToken);
    }

    /// <summary>The <c>TaskUpdate</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("TaskUpdate",
        "Changes one or more tasks: set a status, rename a task, edit its description, or change which tasks block it. " +
        "Mark a task in_progress when you start it and completed as soon as it is finished; put several changes in one call, " +
        "for example completing one task and starting the next. A task cannot be in_progress or completed while a task it waits on is still open, " +
        "and with the default setting only one task can be in_progress. If any entry is invalid nothing changes and it returns 'Error: <what to fix>'.",
        Kind = ToolKind.State)]
    public Task<string> TaskUpdate(
        [Description("The changes, one entry per task, each with the task's id and only the fields to change.")] TaskUpdateInput[] updates,
        CancellationToken cancellationToken = default)
    {
        if (updates is null || updates.Length == 0)
        {
            return Task.FromResult("Error: Give at least one update. Nothing was changed.");
        }

        return MutateAsync(work =>
        {
            HashSet<int> seen = [];
            List<string> parts = [];
            foreach (TaskUpdateInput? update in updates)
            {
                if (update is null)
                {
                    return (Error("An update entry is empty."), string.Empty);
                }

                if (!seen.Add(update.Id))
                {
                    return (Error($"#{update.Id} appears twice in one call; combine the changes into one entry."), string.Empty);
                }

                TaskItem? item = work.Items.Find(candidate => candidate.Id == update.Id);
                if (item is null)
                {
                    return (Error($"There is no task #{update.Id}."), string.Empty);
                }

                if (update.Status is null && update.Subject is null && update.Description is null
                    && update.AddBlockedBy is not { Length: > 0 } && update.RemoveBlockedBy is not { Length: > 0 })
                {
                    return (Error($"The update for #{update.Id} changes nothing; give a status, subject, description or blockers."), string.Empty);
                }

                List<string> changes = [];
                if (update.Status is { } status && status != item.Status)
                {
                    item.Status = status;
                    changes.Add("→ " + TaskListFormatter.StatusName(status));
                }

                if (update.Subject is not null)
                {
                    if (string.IsNullOrWhiteSpace(update.Subject))
                    {
                        return (Error($"The new subject of #{update.Id} is blank."), string.Empty);
                    }
                    item.Subject = update.Subject.Trim();
                    changes.Add("renamed");
                }

                if (update.Description is not null)
                {
                    item.Description = CleanDescription(update.Description);
                    changes.Add("described");
                }

                foreach (int blocker in update.AddBlockedBy ?? [])
                {
                    if (blocker == item.Id)
                    {
                        return (Error($"#{item.Id} cannot wait on itself."), string.Empty);
                    }

                    if (!work.Items.Exists(candidate => candidate.Id == blocker))
                    {
                        return (Error($"#{item.Id} cannot wait on #{blocker}: there is no such task."), string.Empty);
                    }

                    if (!item.BlockedBy.Contains(blocker))
                    {
                        item.BlockedBy.Add(blocker);
                        changes.Add($"waits on #{blocker}");
                    }
                }

                foreach (int blocker in update.RemoveBlockedBy ?? [])
                {
                    if (item.BlockedBy.Remove(blocker))
                    {
                        changes.Add($"no longer waits on #{blocker}");
                    }
                }

                parts.Add(changes.Count == 0 ? $"#{item.Id} unchanged" : $"#{item.Id} {string.Join(", ", changes)}");
            }

            return (null, $"Updated {string.Join("; ", parts)}.");
        }, cancellationToken);
    }

    /// <summary>The <c>TaskRemove</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("TaskRemove",
        "Removes tasks from your task list for good, for example when they turned out to be unnecessary. " +
        "Tasks that waited on a removed task no longer wait on it. Don't remove tasks just because they are completed; " +
        "completed tasks are kept and counted. If an id does not exist nothing is removed and it returns 'Error: <what to fix>'.",
        Kind = ToolKind.State)]
    public Task<string> TaskRemove(
        [Description("The ids of the tasks to remove, as shown in the task list; at least one.")] int[] ids,
        CancellationToken cancellationToken = default)
    {
        if (ids is null || ids.Length == 0)
        {
            return Task.FromResult("Error: Give at least one task id. Nothing was changed.");
        }

        return MutateAsync(work =>
        {
            int[] distinct = [.. ids.Distinct()];
            foreach (int id in distinct)
            {
                if (!work.Items.Exists(item => item.Id == id))
                {
                    return (Error($"There is no task #{id}."), string.Empty);
                }
            }

            work.Items.RemoveAll(item => distinct.Contains(item.Id));
            foreach (TaskItem item in work.Items)
            {
                item.BlockedBy.RemoveAll(distinct.Contains);
            }

            return (null, $"Removed {TaskListFormatter.Ids(distinct)}.");
        }, cancellationToken);
    }

    /// <summary>The <c>TaskGet</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("TaskGet",
        "Returns the full detail of tasks, including descriptions and what blocks them. " +
        "Call it with ids to read specific tasks, or with no ids to read the whole list, including completed tasks and any open tasks " +
        "the shown task list leaves out. You don't need it for the task list that is already shown to you.",
        Kind = ToolKind.Read)]
    public async Task<string> TaskGet(
        [Description("The ids of the tasks to read. Leave it out to read every task.")] int[]? ids = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TaskListSnapshot snapshot = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return TaskListFormatter.FormatDetail(snapshot, ids ?? []);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger?.LogError(error, "The task list could not be read from its storage.");
            return "Error: The task list could not be read. Tell the user if this keeps happening.";
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Runs a change on a copy of the list and, when it and the list rules pass, saves it and makes it the current list.
    /// The change returns an error text, or null with the answer for the model.
    /// </summary>
    private async Task<string> MutateAsync(Func<TaskListSnapshot, (string? Error, string Message)> change, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TaskListSnapshot work;
            try
            {
                work = (await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false)).Clone();
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger?.LogError(error, "The task list could not be read from its storage.");
                return "Error: The task list could not be read. Nothing was changed; tell the user if this keeps happening.";
            }

            (string? problem, string message) = change(work);
            problem ??= CheckRules(work);
            if (problem is not null)
            {
                return problem + " Nothing was changed.";
            }

            if (options.PerSession)
            {
                // Stored again, not changed in place, so the list also survives saving and restoring the session.
                SetSessionState(work);
                return message + NextHint(work);
            }

            try
            {
                await storage.SaveAsync(work, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger?.LogError(error, "The task list could not be saved to its storage.");
                return "Error: The task list could not be saved. Nothing was changed; tell the user if this keeps happening.";
            }

            current = work;
            return message + NextHint(work);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Returns a copy of the task list of <paramref name="session"/>, for a host that shows it outside a run, such as a
    /// <c>/tasks</c> command. For a collection built with <see cref="TaskListToolCollectionBuilder.PerSession"/>.
    /// </summary>
    /// <param name="session">The session whose list to read.</param>
    /// <returns>The list; empty when the session has none yet. Changing the copy changes nothing.</returns>
    /// <exception cref="InvalidOperationException">The collection keeps one list for every session; the message names the fix.</exception>
    public TaskListSnapshot GetTasks(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!options.PerSession)
        {
            throw new InvalidOperationException(
                "This task list is shared by every session, so it has no list of one session. Build it with new TaskListToolCollectionBuilder().PerSession().Build(), " +
                "or read the shared list from the storage you gave it.");
        }
        return Normalize(SessionState<TaskListSnapshot>(session)?.Clone() ?? new TaskListSnapshot());
    }

    /// <summary>The list of the session of the run in progress, or the one read from the storage on first use, cleaned up; call it holding the gate.</summary>
    private async ValueTask<TaskListSnapshot> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (options.PerSession)
        {
            return Normalize(SessionState(() => new TaskListSnapshot()));
        }

        if (current is not null)
        {
            return current;
        }

        TaskListSnapshot loaded = await storage.LoadAsync(cancellationToken).ConfigureAwait(false) ?? new TaskListSnapshot();
        current = Normalize(loaded);
        return current;
    }

    /// <summary>Repairs a stored list: no null lists, no duplicate ids, no blockers that do not exist, and a next id beyond every id.</summary>
    private static TaskListSnapshot Normalize(TaskListSnapshot snapshot)
    {
        snapshot.Items ??= [];
        HashSet<int> ids = [];
        snapshot.Items.RemoveAll(item => item is null || !ids.Add(item.Id));
        foreach (TaskItem item in snapshot.Items)
        {
            item.Subject ??= string.Empty;
            item.BlockedBy = [.. (item.BlockedBy ?? []).Where(id => id != item.Id && ids.Contains(id)).Distinct()];
        }

        snapshot.NextId = Math.Max(snapshot.NextId, ids.Count == 0 ? 1 : ids.Max() + 1);
        return snapshot;
    }

    /// <summary>Checks the whole list after a change: no waiting loops, no started task behind an open one, one task in progress at most.</summary>
    private string? CheckRules(TaskListSnapshot work)
    {
        string? loop = FindLoop(work);
        if (loop is not null)
        {
            return $"Error: Those blockers make tasks wait on each other in a loop ({loop}).";
        }

        foreach (TaskItem item in work.Items.Where(item => item.Status != TaskItemStatus.Pending))
        {
            List<int> open = TaskListFormatter.OpenBlockers(work, item);
            if (open.Count > 0)
            {
                return $"Error: #{item.Id} can't be {TaskListFormatter.StatusName(item.Status)} while it waits on open {TaskListFormatter.Ids(open)}. " +
                       "Complete those first, or drop the blocker with removeBlockedBy.";
            }
        }

        List<int> started = [.. work.Items.Where(item => item.Status == TaskItemStatus.InProgress).Select(item => item.Id)];
        if (options.SingleInProgress && started.Count > 1)
        {
            return $"Error: Only one task can be in_progress at a time; {TaskListFormatter.Ids(started)} would be. Complete or reset the others first.";
        }

        return null;
    }

    /// <summary>Finds tasks that wait on each other in a circle, as 'a blocked by b blocked by a'; null when there is none.</summary>
    private static string? FindLoop(TaskListSnapshot work)
    {
        Dictionary<int, TaskItem> byId = work.Items.ToDictionary(item => item.Id);
        Dictionary<int, bool> finished = [];
        List<int> path = [];

        string? Visit(int id)
        {
            if (finished.TryGetValue(id, out bool done))
            {
                return done ? null : string.Join(" blocked by ", path.Skip(path.IndexOf(id)).Append(id).Select(number => "#" + number));
            }

            finished[id] = false;
            path.Add(id);
            foreach (int blocker in byId[id].BlockedBy.Where(byId.ContainsKey))
            {
                string? loop = Visit(blocker);
                if (loop is not null)
                {
                    return loop;
                }
            }

            path.RemoveAt(path.Count - 1);
            finished[id] = true;
            return null;
        }

        foreach (int id in byId.Keys)
        {
            string? loop = Visit(id);
            if (loop is not null)
            {
                return loop;
            }
        }

        return null;
    }

    /// <summary>Reads a <c>blockedBy</c> entry of <c>TaskAdd</c>: "@n" is the n-th task of the call, anything else an existing task id.</summary>
    private static bool TryResolveBlocker(string? raw, int firstNewId, int batchSize, TaskListSnapshot work, out int id, out string? problem)
    {
        id = 0;
        problem = null;
        string text = (raw ?? string.Empty).Trim().TrimStart('#');
        if (text.StartsWith('@'))
        {
            if (int.TryParse(text.AsSpan(1), out int position) && position >= 1 && position <= batchSize)
            {
                id = firstNewId + position - 1;
                return true;
            }

            problem = $"blockedBy \"{raw}\" is not a task of this call; use @1 to @{batchSize}.";
            return false;
        }

        if (int.TryParse(text, out int existing) && work.Items.Exists(item => item.Id == existing))
        {
            id = existing;
            return true;
        }

        problem = $"blockedBy \"{raw}\" is not an existing task id; use an id from the list, or @n for a task of this call.";
        return false;
    }

    /// <summary>A short pointer after a change: what to start next when nothing is in progress, or that everything is done.</summary>
    private static string NextHint(TaskListSnapshot work)
    {
        if (work.Items.Count > 0 && work.Items.All(item => item.Status == TaskItemStatus.Completed))
        {
            return " All tasks completed.";
        }

        if (work.Items.Any(item => item.Status == TaskItemStatus.InProgress))
        {
            return string.Empty;
        }

        TaskItem? next = work.Items.Find(item => item.Status == TaskItemStatus.Pending && TaskListFormatter.OpenBlockers(work, item).Count == 0);
        return next is null ? string.Empty : $" Next: #{next.Id}.";
    }

    private static string Error(string text) => "Error: " + text;

    private static string? CleanDescription(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
