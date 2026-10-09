namespace PinkRooster.Agents.Eventing;

/// <summary>A tool began running. Pair it with its <see cref="ToolCallCompleted"/> by <paramref name="CallId"/>; parallel calls interleave.</summary>
public sealed record ToolCallStarted(string CallId, string Name, IReadOnlyDictionary<string, object?> Arguments) : AgentEvent;
