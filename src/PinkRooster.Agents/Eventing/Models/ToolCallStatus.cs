namespace PinkRooster.Agents.Eventing;

/// <summary>How a tool call ended.</summary>
public enum ToolCallStatus
{
    /// <summary>The tool returned.</summary>
    Succeeded,

    /// <summary>The tool threw.</summary>
    Failed,

    /// <summary>Approval was refused, so the tool never ran.</summary>
    Rejected,

    /// <summary>The caller's cancellation token was cancelled while the tool ran.</summary>
    Cancelled
}
