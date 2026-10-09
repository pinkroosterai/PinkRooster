namespace PinkRooster.Agents.Permissions;

/// <summary>What the user answered when a <see cref="PermissionPolicy"/> asked about a tool call.</summary>
public enum ApprovalChoice
{
    /// <summary>The call does not run.</summary>
    Skip,

    /// <summary>This call runs.</summary>
    Allow,

    /// <summary>This call runs, and so does every later call the question's <see cref="ApprovalQuestion.SessionRule"/> covers, until the policy is reset.</summary>
    AllowForSession
}
