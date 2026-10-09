
namespace PinkRooster.Agents.Eventing;

/// <summary>A tool call ended: it returned, threw, was refused or was cancelled.</summary>
/// <param name="CallId">Matches the call's earlier events.</param>
/// <param name="Name">The tool's name.</param>
/// <param name="Status">How it ended.</param>
/// <param name="Result">What the tool returned; for <see cref="ToolCallStatus.Rejected"/>, the refusal reason when one was given.</param>
/// <param name="Error">The exception for <see cref="ToolCallStatus.Failed"/> and <see cref="ToolCallStatus.Cancelled"/>.</param>
/// <param name="Duration">How long the tool ran; zero for a refused call.</param>
public sealed record ToolCallCompleted(string CallId, string Name, ToolCallStatus Status, object? Result, Exception? Error, TimeSpan Duration) : AgentEvent;
