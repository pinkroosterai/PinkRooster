namespace PinkRooster.Agents.Permissions;

/// <summary>The answer to a tool call that needs approval: whether it runs, and for a refusal what the model is told.</summary>
/// <param name="Approved">True lets the call run.</param>
/// <param name="Reason">What a refused call tells the model; null tells it the user did not allow the call. Not used for an approved call.</param>
public readonly record struct ApprovalAnswer(bool Approved, string? Reason = null)
{
    /// <summary>The call runs.</summary>
    public static ApprovalAnswer Allow { get; } = new(true);

    /// <summary>The call does not run, and the model reads <paramref name="reason"/> as the tool's answer; null tells it the user did not allow the call.</summary>
    public static ApprovalAnswer Refuse(string? reason = null) => new(false, reason);
}
