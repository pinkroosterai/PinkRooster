namespace PinkRooster.Agents.Eventing;

/// <summary>
/// A run was about to call the model again because a task ended, but it had done so as often as its limit allows. The run ends with
/// its last reply, and the tasks still running are cancelled.
/// </summary>
/// <param name="Limit">The limit, <c>BackgroundOptions.MaxWakes</c>.</param>
/// <param name="CancelledTaskIds">The tasks that were still running.</param>
public sealed record BackgroundWakeLimitReached(int Limit, IReadOnlyList<int> CancelledTaskIds) : AgentEvent;
