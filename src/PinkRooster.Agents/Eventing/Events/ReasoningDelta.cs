namespace PinkRooster.Agents.Eventing;

/// <summary>A piece of the model's reasoning, in arrival order. A non-streaming call publishes one piece per block.</summary>
public sealed record ReasoningDelta(string Text) : AgentEvent;
