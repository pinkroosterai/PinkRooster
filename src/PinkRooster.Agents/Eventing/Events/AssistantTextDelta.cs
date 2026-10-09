namespace PinkRooster.Agents.Eventing;

/// <summary>A piece of the answer text, in arrival order. A non-streaming call publishes one piece per block.</summary>
public sealed record AssistantTextDelta(string Text) : AgentEvent;
