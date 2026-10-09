using Microsoft.Agents.AI;
using PinkRooster.Agents.Permissions;

namespace PinkRooster.SpectreConsole;

/// <summary>What a run that takes typed input can be given besides the prompt and the console.</summary>
public sealed record ConsoleRunOptions
{
    /// <summary>
    /// The policy that answers each tool call that needs approval, asking the user where its mode and rules do not decide. Null has the
    /// console ask allow or skip about every such call.
    /// </summary>
    public PermissionPolicy? Policy { get; init; }

    /// <summary>
    /// Says which typed lines are not for the model, such as <c>line => line.StartsWith('/')</c> for a host's commands. Such a line is
    /// not posted to the agent; it is drawn as held and handed back in <see cref="ConsoleRunResult.UnreadLines"/> when the run has ended.
    /// </summary>
    public Func<string, bool>? HoldLine { get; init; }

    /// <summary>Options for every run of the loop.</summary>
    public AgentRunOptions? RunOptions { get; init; }
}
