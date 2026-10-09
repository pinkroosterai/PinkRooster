using PinkRooster.Agents.Permissions;
using PinkRooster.Samples.CodingAgent.Project;
using PinkRooster.ToolCollections;

namespace PinkRooster.Samples.CodingAgent.Permissions;

/// <summary>Makes the one permission policy that answers the assistant's approvals and its helpers'.</summary>
public static class PermissionSetup
{
    /// <param name="ask">Asks the user about one call; the console's <c>ConfirmToolCallAsync</c>.</param>
    /// <param name="config">The project's allow rules and verify command.</param>
    /// <param name="tools">Every collection the assistant or a helper can call, so the policy knows each tool's kind.</param>
    public static PermissionPolicy Create(Func<ApprovalQuestion, CancellationToken, Task<ApprovalChoice>> ask, ProjectConfig config, params ToolCollection[] tools)
    {
        PermissionPolicy policy = new PermissionPolicy(ask)
            .WithTools(tools)
            // "Allow for this session" on a shell command keeps the start of the command, not the whole tool.
            .WithCommandArgument("RunShell", "command")
            .Allow([.. config.AllowRules]);

        // The project's own verify command runs unasked: the team wrote all of it, so it is allowed as a whole, chained or not.
        if (config.VerifyCommand is string verify)
        {
            policy.Allow(AllowRule.ForWholeText("RunShell", "command", verify));
        }
        return policy;
    }
}
