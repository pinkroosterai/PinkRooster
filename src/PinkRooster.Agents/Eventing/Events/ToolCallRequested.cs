namespace PinkRooster.Agents.Eventing;

/// <summary>The model asked for a tool call. Published once per call, before approval or execution.</summary>
public sealed record ToolCallRequested(string CallId, string Name, IReadOnlyDictionary<string, object?> Arguments) : AgentEvent;
