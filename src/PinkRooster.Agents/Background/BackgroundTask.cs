using System.Diagnostics;
using PinkRooster.Agents.Eventing;

namespace PinkRooster.Agents.Background;

/// <summary>One background call of a tool. Its state changes only inside <see cref="BackgroundTaskStore"/>, under the store's lock.</summary>
internal sealed class BackgroundTask(int id, string toolName, RunScope? scope)
{
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long started = Stopwatch.GetTimestamp();

    public int Id { get; } = id;

    public string ToolName { get; } = toolName;

    /// <summary>The run that started the task; its handlers hear the task's events.</summary>
    public RunScope? Scope { get; } = scope;

    public CancellationTokenSource Cancellation { get; } = new();

    public BackgroundTaskState Status { get; set; } = BackgroundTaskState.Running;

    /// <summary>Why the task was asked to stop: cancelled or timed out. Null while nobody asked.</summary>
    public BackgroundTaskState? StopReason { get; set; }

    /// <summary>What the tool returned, as the tool gave it.</summary>
    public object? Result { get; set; }

    /// <summary>The tool's result as text, for a completed task.</summary>
    public string? ResultText { get; set; }

    /// <summary>What the tool threw, for a failed task.</summary>
    public Exception? Error { get; set; }

    /// <summary>The tool was asked to stop and had not returned after the grace period.</summary>
    public bool DidNotStop { get; set; }

    /// <summary>How many <c>WaitForTasks</c> calls are waiting on the task; one of them tells the model how it ended.</summary>
    public int Waiters { get; set; }

    /// <summary>The model was told how the task ended, by a tool's answer or by the task update; whichever comes first is the only one.</summary>
    public bool Told { get; set; }

    /// <summary>How long the task has run, or ran.</summary>
    public TimeSpan Duration { get; private set; }

    public TimeSpan Age => Status == BackgroundTaskState.Running ? Stopwatch.GetElapsedTime(started) : Duration;

    /// <summary>Completes when the task has ended.</summary>
    public Task Ended => ended.Task;

    public void StopClock() => Duration = Stopwatch.GetElapsedTime(started);

    public void SignalEnded() => ended.TrySetResult();

    /// <summary>The text of the task update, such as <c>Background task 3 (RunSubAgent) completed. Read its result with GetTaskResult.</c></summary>
    public string UpdateText() => Status == BackgroundTaskState.Completed
        ? $"Background task {Id} ({ToolName}) {StatusWords()}. Read its result with GetTaskResult."
        : $"Background task {Id} ({ToolName}) {StatusWords()}.";

    /// <summary>Such as <c>task 3 (RunSubAgent): completed</c>.</summary>
    public string StatusLine() => $"task {Id} ({ToolName}): {StatusWords()}";

    public string StatusWords() => Status switch
    {
        BackgroundTaskState.Running => StopReason is null ? "running" : "running, asked to stop",
        BackgroundTaskState.Completed => "completed",
        BackgroundTaskState.Failed => "failed",
        BackgroundTaskState.Cancelled => DidNotStop ? "cancelled, but it did not stop" : "cancelled",
        _ => DidNotStop ? "timed out, but it did not stop" : "timed out"
    };
}
