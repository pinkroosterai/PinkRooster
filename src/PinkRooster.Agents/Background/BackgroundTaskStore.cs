using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Agents.Eventing;

namespace PinkRooster.Agents.Background;

/// <summary>
/// The background tasks and the inbox of one agent on one session: starts tasks, stops them, keeps their results until the run ends,
/// and posts to the inbox.
/// </summary>
/// <remarks>
/// A task belongs to the run that started it. <see cref="BackgroundRunAgent"/> opens the store when a run begins and closes it when
/// the run ends; a run that pauses for an approval leaves its tasks running for the run that carries the answers.
/// </remarks>
internal sealed class BackgroundTaskStore
{
    /// <summary>The author name on every task update, so a reader of the history can tell them from the user's messages.</summary>
    public const string UpdateAuthor = "background-tasks";

    private static readonly AsyncLocal<BackgroundTaskStore?> current = new();

    private readonly object gate = new();
    private readonly List<BackgroundTask> tasks = [];
    private readonly List<ChatMessage> heldUpdates = [];
    private BackgroundOptions options = new();
    private Func<ChatMessage, Task>? enqueue;
    private ILogger? logger;
    private TaskCompletionSource change = NewSignal();
    private TaskCompletionSource hostPosted = NewSignal();
    private int lastId;
    private int wakes;
    private bool hostPostPending;
    // The host's posts: how many are on their way into the inbox, how many arrived, and whether the run can still read one.
    private int hostPostsInFlight;
    private long hostPosts;
    private bool inboxOpen;
    private bool running;
    private bool paused;
    private bool closing;

    /// <summary>
    /// The store of the run in progress on the current async flow, or null outside a run of an agent with background tools or an inbox.
    /// It lives on the async flow, so a tool call finds its own agent's store, also when several agents share a session.
    /// </summary>
    public static BackgroundTaskStore? Current
    {
        get => current.Value;
        set => current.Value = value;
    }

    public BackgroundOptions Options => options;

    /// <summary>Whether a failed task's exception message is shown to the model, as the agent's tool loop does for a foreground call.</summary>
    public bool ShowsErrorDetails { get; private set; }

    /// <summary>
    /// Opens the store for a run. A run that continues a paused one keeps its tasks and gets the task updates held during the pause;
    /// any other run stops what was left first.
    /// </summary>
    /// <param name="runOptions">The limits of the agent that runs.</param>
    /// <param name="answersApprovals">The run's input holds nothing but approval answers.</param>
    /// <param name="showErrorDetails">The agent's tool loop shows the model exception messages.</param>
    /// <param name="enqueueMessage">Puts a message in the session's inbox.</param>
    /// <param name="runLogger">Where a failed task and a failed post are logged.</param>
    /// <exception cref="InvalidOperationException">A run is already in progress on the session.</exception>
    public async Task BeginRunAsync(BackgroundOptions runOptions, bool answersApprovals, bool showErrorDetails, Func<ChatMessage, Task> enqueueMessage, ILogger? runLogger)
    {
        bool continues;
        ChatMessage[] held;
        lock (gate)
        {
            if (running)
            {
                throw new InvalidOperationException(
                    "A run is already in progress on this session, and its background tasks belong to it. Wait for that run to end, or use another session.");
            }
            running = true;
            continues = paused && answersApprovals;
            paused = false;
            held = continues ? [.. heldUpdates] : [];
            heldUpdates.Clear();
        }
        if (!continues)
        {
            await StopAllAsync().ConfigureAwait(false);
        }

        options = runOptions;
        ShowsErrorDetails = showErrorDetails;
        enqueue = enqueueMessage;
        logger = runLogger;
        lock (gate)
        {
            wakes = 0;
            hostPostPending = false;
            inboxOpen = true;
        }
        foreach (ChatMessage update in held)
        {
            await PostAsync(update, fromHost: false).ConfigureAwait(false);
        }
    }

    /// <summary>Leaves the tasks running for the run that will carry the approval answers; their task updates are held until then.</summary>
    public void Pause()
    {
        lock (gate)
        {
            paused = true;
            running = false;
        }
    }

    /// <summary>Stops every running task, waits until each has ended or used up its grace period, and forgets all tasks.</summary>
    public async Task EndRunAsync()
    {
        await StopAllAsync().ConfigureAwait(false);
        lock (gate)
        {
            running = false;
        }
    }

    private async Task StopAllAsync()
    {
        BackgroundTask[] all;
        lock (gate)
        {
            // A task stopped because its run is over has nobody left to tell.
            closing = true;
            all = [.. tasks];
        }
        foreach (BackgroundTask task in all)
        {
            RequestStop(task, BackgroundTaskState.Cancelled);
        }
        await Task.WhenAll(all.Select(task => task.Ended)).ConfigureAwait(false);
        lock (gate)
        {
            // The ids go on counting: an id in the session's history never comes to mean another task.
            tasks.Clear();
            heldUpdates.Clear();
            closing = false;
        }
    }

    /// <summary>Starts <paramref name="function"/> as a task and returns the text the model reads: the task's id, or why nothing started.</summary>
    public string Start(AIFunction function, AIFunctionArguments arguments)
    {
        BackgroundTask task;
        lock (gate)
        {
            int runningTasks = tasks.Count(candidate => candidate.Status == BackgroundTaskState.Running);
            if (runningTasks >= options.MaxRunningTasks)
            {
                return $"Error: {runningTasks} background tasks are running, and at most {options.MaxRunningTasks} may run at once. Nothing was started. " +
                    "Wait for one to end with WaitForTasks, cancel one with CancelTask, or make this call without runInBackground.";
            }
            task = new BackgroundTask(++lastId, function.Name, RunScope.Current);
            tasks.Add(task);
        }

        string? callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;
        task.Scope?.Publish(() => new BackgroundTaskStarted(task.Id, task.ToolName, callId));
        // A time limit that passes is a stop nobody asked for by name; the registration below records it as timed out.
        task.Cancellation.Token.Register(() => OnStopRequested(task, BackgroundTaskState.TimedOut));
        task.Cancellation.CancelAfter(options.TaskTimeLimit);
        // Task.Run keeps the async flow, so a sub-agent started here still finds the calling run as its parent.
        _ = Task.Run(() => RunAsync(task, function, arguments));
        return $"Started task {task.Id} ({task.ToolName}). WaitForTasks returns its result once it has ended.";
    }

    public BackgroundTask? Find(int id)
    {
        lock (gate)
        {
            return tasks.FirstOrDefault(task => task.Id == id);
        }
    }

    public BackgroundTask[] All()
    {
        lock (gate)
        {
            return [.. tasks];
        }
    }

    /// <summary>Whether a run is in progress on the session; false during an approval pause.</summary>
    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return running;
            }
        }
    }

    public int[] RunningTaskIds()
    {
        lock (gate)
        {
            return [.. tasks.Where(task => task.Status == BackgroundTaskState.Running).Select(task => task.Id)];
        }
    }

    /// <summary>Completes when a task next ends or a message is next posted. Take it before looking at the state it guards.</summary>
    public Task NextChange()
    {
        lock (gate)
        {
            return change.Task;
        }
    }

    /// <summary>Completes when the host next posts a message to the inbox. A task update does not complete it.</summary>
    public Task NextHostPost()
    {
        lock (gate)
        {
            return hostPosted.Task;
        }
    }

    /// <summary>
    /// Counts a wake: the model being called again because a task ended after it had ended its turn. A call made because the host posted
    /// a message is not counted.
    /// </summary>
    /// <returns>False when the wake would pass <see cref="BackgroundOptions.MaxWakes"/>; the run then ends.</returns>
    public bool TryCountWake()
    {
        lock (gate)
        {
            if (hostPostPending)
            {
                hostPostPending = false;
                return true;
            }
            if (wakes >= options.MaxWakes)
            {
                return false;
            }
            wakes++;
            return true;
        }
    }

    /// <summary>Logs that the limit on wakes ends the run with tasks still running.</summary>
    public void LogWakeLimit(IReadOnlyList<int> cancelled) =>
        logger?.LogWarning(
            "The run reached its limit of {MaxWakes} wakes with {Count} background tasks still running; they are cancelled. Raise BackgroundOptions.MaxWakes to wait longer.",
            options.MaxWakes, cancelled.Count);

    /// <summary>
    /// Registers a <c>WaitForTasks</c> call on <paramref name="waited"/>, unless what it waits for has already happened. While it is
    /// registered, the call is the one that tells the model how a listed task ended, so none of them posts a task update.
    /// </summary>
    /// <param name="waited">The listed tasks.</param>
    /// <param name="forAll">The call waits until every listed task has ended, not only the first.</param>
    /// <returns>False, with nothing registered, when there is nothing left to wait for.</returns>
    public bool TryBeginWait(IReadOnlyList<BackgroundTask> waited, bool forAll)
    {
        lock (gate)
        {
            bool done = forAll
                ? waited.All(task => task.Status != BackgroundTaskState.Running)
                : waited.Any(task => task.Status != BackgroundTaskState.Running);
            if (done)
            {
                return false;
            }
            foreach (BackgroundTask task in waited)
            {
                task.Waiters++;
            }
            return true;
        }
    }

    public void EndWait(IReadOnlyList<BackgroundTask> waited)
    {
        lock (gate)
        {
            foreach (BackgroundTask task in waited)
            {
                task.Waiters--;
            }
        }
    }

    /// <summary>
    /// Call before a tool's answer says how <paramref name="reported"/> stand. A task that has ended and whose task update is not on
    /// its way is marked as told, so no update follows; one whose update is on its way is waited for, so the update is in the inbox
    /// before the answer is.
    /// </summary>
    public async Task TellAsync(IReadOnlyList<BackgroundTask> reported)
    {
        BackgroundTask[] ended;
        lock (gate)
        {
            ended = [.. reported.Where(task => task.Status != BackgroundTaskState.Running)];
            foreach (BackgroundTask task in ended)
            {
                task.Told = true;
            }
        }
        await Task.WhenAll(ended.Select(task => task.Ended)).ConfigureAwait(false);
    }

    /// <summary>Puts a message in the session's inbox and wakes a run that waits for one.</summary>
    /// <param name="message">The message.</param>
    /// <param name="fromHost">The host posted it; such a message also ends a waiting <c>WaitForTasks</c> call and is not counted as a wake.</param>
    public async Task PostAsync(ChatMessage message, bool fromHost)
    {
        if (enqueue is not Func<ChatMessage, Task> send)
        {
            throw new InvalidOperationException("The inbox is not open; a run of an agent with an inbox must have begun on this session.");
        }
        await send(message).ConfigureAwait(false);
        Signal(fromHost);
    }

    /// <summary>
    /// Puts the host's message in the inbox when a run will read it, and says whether it did: the run in progress, or during an
    /// approval pause the run that carries the answers. False when no run is in progress and none is paused, and once the wake has
    /// decided that the run is over.
    /// </summary>
    public async Task<bool> TryPostFromHostAsync(ChatMessage message)
    {
        Func<ChatMessage, Task> send;
        lock (gate)
        {
            if (!(paused || (running && inboxOpen)) || enqueue is null)
            {
                return false;
            }
            hostPostsInFlight++;
            send = enqueue;
        }

        try
        {
            await send(message).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                hostPostsInFlight--;
                hostPosts++;
            }
        }
        Signal(fromHost: true);
        return true;
    }

    /// <summary>How many of the host's posts have arrived. Read it before looking at the inbox, and give it to <see cref="TryCloseInbox"/>.</summary>
    public long HostPosts
    {
        get
        {
            lock (gate)
            {
                return hostPosts;
            }
        }
    }

    /// <summary>
    /// Closes the inbox to the host for the rest of the run, unless a post arrived since <paramref name="seenHostPosts"/> was read or
    /// one is on its way: then the inbox has, or is about to have, a message the caller has not seen, and it must look again.
    /// </summary>
    public bool TryCloseInbox(long seenHostPosts)
    {
        lock (gate)
        {
            if (hostPostsInFlight > 0 || hostPosts != seenHostPosts)
            {
                return false;
            }
            inboxOpen = false;
            return true;
        }
    }

    /// <summary>Closes the inbox to the host for the rest of the run, whatever it holds.</summary>
    public void CloseInbox()
    {
        lock (gate)
        {
            inboxOpen = false;
        }
    }

    /// <summary>Asks a running task to stop. It ends when its tool returns, or after the grace period, whichever comes first.</summary>
    /// <param name="task">The task to stop.</param>
    /// <param name="reason">Cancelled or timed out.</param>
    /// <param name="toldByCaller">The caller's own answer tells the model how the task ended, so no task update follows.</param>
    public void RequestStop(BackgroundTask task, BackgroundTaskState reason, bool toldByCaller = false)
    {
        lock (gate)
        {
            if (task.Status != BackgroundTaskState.Running)
            {
                return;
            }
            task.Told |= toldByCaller;
            if (task.StopReason is not null)
            {
                return;
            }
            task.StopReason = reason;
        }
        task.Cancellation.Cancel();
    }

    private void OnStopRequested(BackgroundTask task, BackgroundTaskState reasonWhenNoneGiven)
    {
        lock (gate)
        {
            if (task.Status != BackgroundTaskState.Running)
            {
                return;
            }
            task.StopReason ??= reasonWhenNoneGiven;
        }
        _ = GiveGraceAsync(task);
    }

    private async Task GiveGraceAsync(BackgroundTask task)
    {
        await Task.WhenAny(task.Ended, Task.Delay(options.GracePeriod)).ConfigureAwait(false);
        // A tool that ignores cancellation must not hold the run open; it is reported as it is and no longer waited for.
        await FinishAsync(task, task.StopReason ?? BackgroundTaskState.TimedOut, null, null, didNotStop: true).ConfigureAwait(false);
    }

    private async Task RunAsync(BackgroundTask task, AIFunction function, AIFunctionArguments arguments)
    {
        try
        {
            object? result = await function.InvokeAsync(arguments, task.Cancellation.Token).ConfigureAwait(false);
            await FinishAsync(task, BackgroundTaskState.Completed, result, null, didNotStop: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (task.Cancellation.IsCancellationRequested)
        {
            // A stop somebody asked for recorded its reason before it cancelled; a cancellation without one is the time limit.
            await FinishAsync(task, task.StopReason ?? BackgroundTaskState.TimedOut, null, null, didNotStop: false).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await FinishAsync(task, BackgroundTaskState.Failed, null, error, didNotStop: false).ConfigureAwait(false);
        }
    }

    private async Task FinishAsync(BackgroundTask task, BackgroundTaskState status, object? result, Exception? error, bool didNotStop)
    {
        lock (gate)
        {
            if (task.Status != BackgroundTaskState.Running)
            {
                return;
            }
            task.StopClock();
            task.Status = status;
            task.Result = result;
            task.ResultText = status == BackgroundTaskState.Completed ? AsText(result) : null;
            task.Error = error;
            task.DidNotStop = didNotStop;
        }
        // Drops the time limit's timer; the task can no longer time out.
        task.Cancellation.CancelAfter(Timeout.InfiniteTimeSpan);
        if (error is not null)
        {
            // In the foreground the tool loop would log this; here nothing else sees the exception.
            logger?.LogError(error, "Background task {TaskId} ({ToolName}) failed.", task.Id, task.ToolName);
        }
        task.Scope?.Publish(() => new BackgroundTaskEnded(task.Id, task.ToolName, status, didNotStop, result, error, task.Duration));

        ChatMessage? update = null;
        bool hold;
        lock (gate)
        {
            // A tool's answer that already told the model, or a waiting WaitForTasks call that will, leaves nothing to post; nor does a closing run.
            if (!task.Told && task.Waiters == 0 && !closing)
            {
                task.Told = true;
                update = new ChatMessage(ChatRole.User, task.UpdateText()) { AuthorName = UpdateAuthor };
            }
            hold = paused;
            if (update is not null && hold)
            {
                heldUpdates.Add(update);
            }
        }

        if (update is not null && !hold)
        {
            try
            {
                await PostAsync(update, fromHost: false).ConfigureAwait(false);
            }
            catch (Exception postError)
            {
                // The result is still there for GetTaskResult and ListTasks; only the telling was lost.
                logger?.LogWarning(postError, "The update for background task {TaskId} ({ToolName}) could not be posted.", task.Id, task.ToolName);
            }
        }
        // Signalled last, so whoever waited on the task finds its event delivered and its update posted.
        task.SignalEnded();
        Signal(fromHost: false);
    }

    private void Signal(bool fromHost)
    {
        TaskCompletionSource changed;
        TaskCompletionSource? host = null;
        lock (gate)
        {
            changed = change;
            change = NewSignal();
            if (fromHost)
            {
                hostPostPending = true;
                host = hostPosted;
                hostPosted = NewSignal();
            }
        }
        changed.TrySetResult();
        host?.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // A tool made from a method returns its value as JSON; text stays text and anything else is read as JSON text.
    private static string AsText(object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions)
    };
}
