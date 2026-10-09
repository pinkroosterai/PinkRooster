using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Eventing;

/// <summary>A run ended. Exactly one per run, and always the last event of its run, whatever the outcome.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Error">The exception for <see cref="RunOutcome.Failed"/> and <see cref="RunOutcome.Cancelled"/>; otherwise null.</param>
/// <param name="Duration">From <see cref="RunStarted"/> to this event.</param>
/// <param name="Usage">The sum of the usage of this run's own model calls, or null when none reported usage. An agent this run called as a tool reports its own usage on its own <see cref="RunCompleted"/>, and it is not included here.</param>
public sealed record RunCompleted(RunOutcome Outcome, Exception? Error = null, TimeSpan Duration = default, UsageDetails? Usage = null) : AgentEvent;
