namespace PinkRooster.Agents.Eventing;

/// <summary>A block of reasoning ended; <paramref name="Text"/> is its deltas joined.</summary>
/// <remarks>A block ends at the next answer text, tool call or end of the model call. A call that fails mid-block completes no block.</remarks>
public sealed record ReasoningCompleted(string Text) : AgentEvent;
