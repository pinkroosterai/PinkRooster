namespace PinkRooster.Agents.Eventing;

/// <summary>A request is about to go to the model. A run makes one or more; each ends with one <see cref="ModelCallCompleted"/>.</summary>
public sealed record ModelCallStarted : AgentEvent;
