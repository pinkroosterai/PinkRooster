
namespace PinkRooster.Agents.Eventing;

/// <summary>A background task ended: its tool returned or threw, or it was cancelled or timed out.</summary>
/// <param name="TaskId">Matches the task's <see cref="BackgroundTaskStarted"/>.</param>
/// <param name="ToolName">The tool that ran.</param>
/// <param name="State">How it ended; never <see cref="BackgroundTaskState.Running"/>.</param>
/// <param name="DidNotStop">True when the tool was asked to stop and had not returned after the grace period; it may still be running.</param>
/// <param name="Result">What the tool returned, for <see cref="BackgroundTaskState.Completed"/>.</param>
/// <param name="Error">The exception, for <see cref="BackgroundTaskState.Failed"/>.</param>
/// <param name="Duration">How long the task ran.</param>
public sealed record BackgroundTaskEnded(int TaskId, string ToolName, BackgroundTaskState State, bool DidNotStop, object? Result, Exception? Error, TimeSpan Duration) : AgentEvent;
