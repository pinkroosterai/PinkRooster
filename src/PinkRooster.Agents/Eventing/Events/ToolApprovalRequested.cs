
namespace PinkRooster.Agents.Eventing;

/// <summary>A tool call waits for approval; the run then ends with <see cref="RunOutcome.AwaitingApproval"/>.</summary>
public sealed record ToolApprovalRequested(string CallId, string Name, IReadOnlyDictionary<string, object?> Arguments) : AgentEvent;
