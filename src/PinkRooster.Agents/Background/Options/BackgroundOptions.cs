namespace PinkRooster.Agents.Background;

/// <summary>The limits on an agent's background tasks and inbox; change them with <see cref="AgentBuilder.ConfigureBackground"/>.</summary>
/// <remarks>Each default is a figure a shipped harness or MAF uses; none was measured for this library.</remarks>
public sealed class BackgroundOptions
{
    /// <summary>The most tasks running at once; a start beyond it is refused with an error the model reads. Default 6.</summary>
    public int MaxRunningTasks { get; set; } = 6;

    /// <summary>How long one task may run before it is stopped and ends as timed out. Default 30 minutes.</summary>
    public TimeSpan TaskTimeLimit { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a task that was asked to stop gets before it is reported as not having stopped and no longer waited for. Default 30 seconds.</summary>
    /// <remarks>
    /// A tool that ignores its cancellation token holds up whoever stops it for this long: a cancelled run returns that much later,
    /// and so does the model's <c>CancelTask</c> call.
    /// </remarks>
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The most characters <c>GetTaskResult</c> returns; a longer result is cut in the middle. Default 20,000.</summary>
    public int MaxResultCharacters { get; set; } = 20_000;

    /// <summary>How long <c>WaitForTasks</c> waits when the model names no timeout. Default 300 seconds.</summary>
    public TimeSpan DefaultWaitTimeout { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>The longest <c>WaitForTasks</c> waits; a longer timeout from the model is shortened to it. Default 1 hour.</summary>
    public TimeSpan MaxWaitTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How often one run may call the model again because a task ended after the model had ended its turn. A call made because the
    /// host posted a message is not counted. At the limit the run ends with its last reply, its running tasks are cancelled, and a
    /// <c>BackgroundWakeLimitReached</c> event and a warning say so. 0 means a run never waits for its tasks. Default 10.
    /// </summary>
    public int MaxWakes { get; set; } = 10;

    /// <summary>Throws for a limit that cannot work, naming the property and the fix.</summary>
    internal void Validate()
    {
        Require(MaxRunningTasks >= 1, nameof(MaxRunningTasks), "at least 1");
        Require(TaskTimeLimit > TimeSpan.Zero, nameof(TaskTimeLimit), "longer than zero");
        Require(GracePeriod > TimeSpan.Zero, nameof(GracePeriod), "longer than zero");
        Require(MaxResultCharacters >= 1, nameof(MaxResultCharacters), "at least 1");
        Require(DefaultWaitTimeout > TimeSpan.Zero, nameof(DefaultWaitTimeout), "longer than zero");
        Require(MaxWakes >= 0, nameof(MaxWakes), "0 or more");
        Require(MaxWaitTimeout >= DefaultWaitTimeout, nameof(MaxWaitTimeout), $"at least {nameof(DefaultWaitTimeout)}");
    }

    private static void Require(bool holds, string property, string rule)
    {
        if (!holds)
        {
            throw new InvalidOperationException(
                $"{nameof(BackgroundOptions)}.{property} must be {rule}; set it in {nameof(AgentBuilder)}.{nameof(AgentBuilder.ConfigureBackground)}.");
        }
    }
}
