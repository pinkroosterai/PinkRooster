using PinkRooster.Agents.Permissions;

namespace PinkRooster.Samples.CodingAgent.Permissions;

/// <summary>The names the user and the model know the permission modes by.</summary>
public static class PermissionModes
{
    public const string Ask = "ask";
    public const string AcceptEdits = "accept-edits";
    public const string Plan = "plan";

    public static IReadOnlyList<string> Names { get; } = [Ask, AcceptEdits, Plan];

    public static string NameOf(PermissionMode mode) => mode switch
    {
        PermissionMode.AcceptEdits => AcceptEdits,
        PermissionMode.Plan => Plan,
        _ => Ask
    };

    public static bool TryParse(string name, out PermissionMode mode)
    {
        (bool known, mode) = name.Trim().ToLowerInvariant() switch
        {
            Ask => (true, PermissionMode.Ask),
            AcceptEdits => (true, PermissionMode.AcceptEdits),
            Plan => (true, PermissionMode.Plan),
            _ => (false, PermissionMode.Ask)
        };
        return known;
    }

    /// <summary>What a mode means, in a line the model reads before every model call and the user reads after <c>/mode</c>.</summary>
    public static string Describe(PermissionMode mode) => mode switch
    {
        PermissionMode.AcceptEdits => "File changes run without asking; the user is asked before a command runs.",
        PermissionMode.Plan => "Explore, and answer with a plan of the change. Change nothing: a file change is refused, and so is a command the project has not allowed.",
        _ => "The user is asked before a file is changed or a command runs."
    };
}
