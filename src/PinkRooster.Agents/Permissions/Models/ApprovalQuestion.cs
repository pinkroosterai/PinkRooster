using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Permissions;

/// <summary>What a <see cref="PermissionPolicy"/> asks the user: whether a tool call may run.</summary>
/// <param name="Call">The call that waits for an answer.</param>
/// <param name="SessionRule">
/// What <see cref="ApprovalChoice.AllowForSession"/> would allow from here on: the tool, or the start of its command. Null when that
/// answer is not on offer, and it is then taken as <see cref="ApprovalChoice.Allow"/>.
/// </param>
public sealed record ApprovalQuestion(FunctionCallContent Call, AllowRule? SessionRule = null);
