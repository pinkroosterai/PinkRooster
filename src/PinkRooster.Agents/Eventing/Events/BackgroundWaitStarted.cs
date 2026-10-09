namespace PinkRooster.Agents.Eventing;

/// <summary>
/// The model ended its turn while background tasks run, and the run now waits, without a model call, until one ends or a message is posted.
/// </summary>
/// <param name="TaskIds">The tasks still running.</param>
public sealed record BackgroundWaitStarted(IReadOnlyList<int> TaskIds) : AgentEvent;
