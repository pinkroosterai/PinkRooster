using System.ComponentModel;
using System.Globalization;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Shared;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Background;

/// <summary>
/// The tools an agent follows its background tasks with: <c>WaitForTasks</c>, <c>GetTaskResult</c>, <c>ListTasks</c> and
/// <c>CancelTask</c>. <see cref="AgentBuilder.AllowBackground"/> adds it; a host does not create it.
/// </summary>
/// <remarks>The collection holds no state: every tool works on the tasks of the run in progress.</remarks>
internal sealed class BackgroundTasksToolCollection : ToolCollection
{
    private readonly BackgroundOptions options;

    internal BackgroundTasksToolCollection(BackgroundOptions options)
    {
        this.options = options;
        AddInstruction(
            "A tool with a runInBackground parameter can run as a background task. Use that for work that takes long while you have other work to do; " +
            "when you need the result before your next step, call the tool without it. " +
            $"At most {options.MaxRunningTasks} tasks run at once, and a task is stopped after {Words(options.TaskTimeLimit)}. " +
            (options.MaxWakes > 0
                ? "You are told when a task ends. When you end your turn while tasks are still running, you are called again once one ends, " +
                  "so wait with WaitForTasks only when a result is the next thing you need."
                : "Tasks still running when you end your turn are cancelled, so wait with WaitForTasks for the ones whose result you need."));
    }

    /// <summary>The <c>WaitForTasks</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("WaitForTasks",
        "Waits for background tasks and returns how each given task stands, with the result of every one that has ended. " +
        "By default it returns when the first of them has ended; set waitForAll to wait until all have. It also returns when the timeout has passed " +
        "or a message has arrived for you; neither is an error, and the unfinished tasks keep running. " +
        "Use it when you have nothing else to do until a result is there; do not call it repeatedly with a short timeout. " +
        "Long results are cut in the middle. A call that could not wait returns 'Error: ...' with what to change.",
        Kind = ToolKind.Read)]
    public async Task<string> WaitForTasks(
        [Description("The ids of the tasks to wait for, from the start messages or from ListTasks.")] int[] taskIds,
        [Description("Optional. True waits until every given task has ended; leave it out to return when the first has.")] bool waitForAll = false,
        [Description("Optional. The most seconds to wait. Leave it out for the default.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        if (BackgroundTaskStore.Current is not BackgroundTaskStore store)
        {
            return NoRun;
        }
        if (taskIds is not { Length: > 0 })
        {
            return "Error: taskIds is empty. Name the tasks to wait for; ListTasks shows them.";
        }
        if (timeoutSeconds is < 1)
        {
            return "Error: timeoutSeconds must be at least 1. Leave it out for the default.";
        }

        List<BackgroundTask> listed = [];
        foreach (int id in taskIds.Distinct())
        {
            if (store.Find(id) is not BackgroundTask task)
            {
                return Unknown(store, id);
            }
            listed.Add(task);
        }

        TimeSpan timeout = timeoutSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : options.DefaultWaitTimeout;
        if (timeout > options.MaxWaitTimeout)
        {
            timeout = options.MaxWaitTimeout;
        }

        bool messageArrived = false;
        if (store.TryBeginWait(listed, waitForAll))
        {
            // Only a message from the host ends the wait early; another task's update reaches the model with its next model call.
            Task hostPost = store.NextHostPost();
            try
            {
                using CancellationTokenSource stopDelay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task delay = Task.Delay(timeout, stopDelay.Token);
                Task ended = waitForAll ? Task.WhenAll(listed.Select(task => task.Ended)) : Task.WhenAny(listed.Select(task => task.Ended));
                Task first = await Task.WhenAny(ended, hostPost, delay).ConfigureAwait(false);
                await stopDelay.CancelAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                messageArrived = first == hostPost;
            }
            finally
            {
                store.EndWait(listed);
            }
        }
        await store.TellAsync(listed).ConfigureAwait(false);

        int endedCount = listed.Count(task => task.Ended.IsCompleted);
        bool done = waitForAll ? endedCount == listed.Count : endedCount > 0;
        // The result limit is for the whole answer, shared by the results in it.
        int each = Math.Max(1, options.MaxResultCharacters / Math.Max(1, endedCount));
        string tasks = string.Join("\n\n", listed.Select(task => Describe(store, task, each)));
        if (done)
        {
            return tasks;
        }
        string what = waitForAll ? "every listed task had ended" : "a listed task ended";
        return messageArrived
            ? $"A message arrived for you before {what}; the unfinished tasks keep running.\n\n{tasks}"
            : $"{Sentence(Words(timeout))} passed before {what}; the unfinished tasks keep running.\n\n{tasks}";
    }

    /// <summary>The <c>GetTaskResult</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("GetTaskResult",
        "Returns how one background task stands, with its result when it completed. WaitForTasks already returns the result of a task that has ended; " +
        "use this to read a result again, or the result of a task you were told about in a message. A very long result is cut in the middle. " +
        "Reading a result does not remove it; it stays until the end of this run. An unknown id returns 'Error: ...' with the ids there are.",
        Kind = ToolKind.Read)]
    public async Task<string> GetTaskResult([Description("The id of the task, from its start message or from ListTasks.")] int taskId)
    {
        if (BackgroundTaskStore.Current is not BackgroundTaskStore store)
        {
            return NoRun;
        }
        if (store.Find(taskId) is not BackgroundTask task)
        {
            return Unknown(store, taskId);
        }

        await store.TellAsync([task]).ConfigureAwait(false);
        return Describe(store, task, options.MaxResultCharacters);
    }

    /// <summary>The <c>ListTasks</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("ListTasks",
        "Lists every background task of this run, one per line, with its id, tool, status and how long it has run or ran. " +
        "Use it when you no longer know which tasks you started or which have ended.",
        Kind = ToolKind.Read)]
    public async Task<string> ListTasks()
    {
        if (BackgroundTaskStore.Current is not BackgroundTaskStore store)
        {
            return NoRun;
        }
        BackgroundTask[] tasks = store.All();
        await store.TellAsync(tasks).ConfigureAwait(false);
        return tasks.Length == 0
            ? "There are no background tasks in this run."
            : string.Join("\n", tasks.Select(task => $"{task.StatusLine()}, {Words(task.Age)}"));
    }

    /// <summary>The <c>CancelTask</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("CancelTask",
        "Stops a running background task whose result you no longer need, and returns its status. Its work so far is lost. " +
        "A task that has already ended is left as it is. An unknown id returns 'Error: ...' with the ids there are.",
        Kind = ToolKind.State)]
    public async Task<string> CancelTask(
        [Description("The id of the task to stop, from its start message or from ListTasks.")] int taskId,
        CancellationToken cancellationToken = default)
    {
        if (BackgroundTaskStore.Current is not BackgroundTaskStore store)
        {
            return NoRun;
        }
        if (store.Find(taskId) is not BackgroundTask task)
        {
            return Unknown(store, taskId);
        }
        await store.TellAsync([task]).ConfigureAwait(false);
        if (task.Ended.IsCompleted)
        {
            return $"Nothing to cancel: {task.StatusLine()}.";
        }

        store.RequestStop(task, BackgroundTaskState.Cancelled, toldByCaller: true);
        // Ends when the tool returns, and at the latest when its grace period is over.
        await task.Ended.WaitAsync(cancellationToken).ConfigureAwait(false);
        await store.TellAsync([task]).ConfigureAwait(false);
        return task.StatusLine();
    }

    private const string NoRun = "Error: No run with background tasks is in progress, so there are no tasks to work with.";

    // How one task stands, in words, with its result when it has one.
    private string Describe(BackgroundTaskStore store, BackgroundTask task, int resultLimit)
    {
        string head = $"Task {task.Id} ({task.ToolName})";
        return task.Status switch
        {
            BackgroundTaskState.Running => $"{head} is still {task.StatusWords()}.",
            BackgroundTaskState.Completed when string.IsNullOrEmpty(task.ResultText) => $"{head} completed and returned nothing.",
            BackgroundTaskState.Completed => $"{head} completed. Its result:\n{TextCut.InTheMiddle(task.ResultText!, resultLimit)}",
            // As in the foreground: the model reads the exception's message only when the agent's tool loop shows error details.
            BackgroundTaskState.Failed when store.ShowsErrorDetails && task.Error is not null => $"{head} failed: {TextCut.InTheMiddle(task.Error.Message, resultLimit)}",
            BackgroundTaskState.Failed => $"{head} failed. It has no result.",
            BackgroundTaskState.Cancelled => $"{head} was cancelled{DidNotStop(task)}. It has no result.",
            _ => $"{head} timed out after {Words(options.TaskTimeLimit)} and was stopped{DidNotStop(task)}. It has no result."
        };
    }

    private static string Unknown(BackgroundTaskStore store, int id)
    {
        BackgroundTask[] tasks = store.All();
        return tasks.Length == 0
            ? $"Error: No task {id}. There are no background tasks in this run."
            : $"Error: No task {id}. The tasks of this run are: {string.Join(", ", tasks.Select(task => task.Id))}.";
    }

    private static string DidNotStop(BackgroundTask task) =>
        task.DidNotStop ? ", but its tool did not stop and may still be running" : string.Empty;

    private static string Sentence(string words) => char.ToUpperInvariant(words[0]) + words[1..];

    // Whole units where they are exact, so a limit reads as '30 minutes' and not '1800 seconds'.
    private static string Words(TimeSpan time)
    {
        if (time.TotalHours >= 1 && time.TotalHours == Math.Floor(time.TotalHours))
        {
            return Count((long)time.TotalHours, "hour");
        }
        if (time.TotalMinutes >= 1 && time.TotalMinutes == Math.Floor(time.TotalMinutes))
        {
            return Count((long)time.TotalMinutes, "minute");
        }
        if (time.TotalSeconds >= 1)
        {
            return Count((long)Math.Round(time.TotalSeconds), "second");
        }
        return Count((long)Math.Round(time.TotalMilliseconds), "millisecond");
    }

    private static string Count(long number, string unit) =>
        $"{number.ToString(CultureInfo.InvariantCulture)} {unit}{(number == 1 ? string.Empty : "s")}";
}
