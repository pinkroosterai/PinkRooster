using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Eventing;

/// <summary>A model call ended: the response came back, the stream ended or was abandoned, or the call failed. One for every <see cref="ModelCallStarted"/>.</summary>
/// <param name="ModelId">The model that answered, when the provider says.</param>
/// <param name="FinishReason">Why the model stopped, when the provider says; null for a failed call.</param>
/// <param name="Usage">The response's usage, or for a stream the sum of its usage items so far; null when the provider sent none.</param>
/// <param name="Duration">From just before the request to the end of the response or stream.</param>
/// <param name="Error">The exception for a failed call; otherwise null.</param>
public sealed record ModelCallCompleted(string? ModelId, ChatFinishReason? FinishReason, UsageDetails? Usage, TimeSpan Duration, Exception? Error) : AgentEvent;
