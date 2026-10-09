namespace PinkRooster.Agents.Eventing;

/// <summary>Where a background task stands. A task starts <see cref="Running"/> and ends in exactly one of the others.</summary>
public enum BackgroundTaskState
{
    /// <summary>The tool has not returned yet.</summary>
    Running,

    /// <summary>The tool returned.</summary>
    Completed,

    /// <summary>The tool threw.</summary>
    Failed,

    /// <summary>The model, the host or the end of the run stopped it.</summary>
    Cancelled,

    /// <summary>It passed <see cref="Background.BackgroundOptions.TaskTimeLimit"/> and was stopped.</summary>
    TimedOut
}
