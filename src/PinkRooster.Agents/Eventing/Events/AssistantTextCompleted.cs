namespace PinkRooster.Agents.Eventing;

/// <summary>A block of answer text ended; <paramref name="Text"/> holds its deltas joined.</summary>
/// <remarks>A block ends at the next reasoning, tool call or end of the model call, so a tool-using run can complete several.</remarks>
public sealed record AssistantTextCompleted(string Text) : AgentEvent;
