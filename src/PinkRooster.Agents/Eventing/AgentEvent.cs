namespace PinkRooster.Agents.Eventing;

/// <summary>Base of every event a builder-made agent publishes: which run of which agent it belongs to, and when it happened.</summary>
/// <remarks>The run fields are set by the agent when it publishes; match on the derived type for what happened.</remarks>
public abstract record AgentEvent
{
    /// <summary>The run the event belongs to: one <c>RunAsync</c> or <c>RunStreamingAsync</c> call. Every event of that call shares it.</summary>
    public Guid RunId { get; init; }

    /// <summary>The run of the agent that called this one as a tool; null for a top-level run.</summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>The publishing agent's <c>Id</c>.</summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>The publishing agent's <c>Name</c>, for display; may be null.</summary>
    public string? AgentName { get; init; }

    /// <summary>When the event happened, in UTC.</summary>
    public DateTimeOffset Timestamp { get; init; }
}
