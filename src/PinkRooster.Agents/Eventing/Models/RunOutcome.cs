
namespace PinkRooster.Agents.Eventing;

/// <summary>How a run ended.</summary>
public enum RunOutcome
{
    /// <summary>The run ended normally.</summary>
    Succeeded,

    /// <summary>The run threw.</summary>
    Failed,

    /// <summary>The caller's cancellation token was cancelled. Any other cancellation, such as an HTTP timeout, is <see cref="Failed"/>.</summary>
    Cancelled,

    /// <summary>The run stopped because a tool call needs approval; send the answers on the same session to continue.</summary>
    AwaitingApproval,

    /// <summary>A streamed run whose consumer stopped reading before the stream ended, without cancelling the token. <see cref="RunCompleted.Error"/> is null.</summary>
    Abandoned
}
