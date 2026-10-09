namespace PinkRooster.SpectreConsole;

/// <summary>The colours, layout limits and secrets of an <see cref="AgentConsole"/>. The defaults are the look of the PinkRooster samples.</summary>
public sealed record AgentConsoleOptions
{
    /// <summary>The colour of prompts, the approval panel and the selection highlight, as a Spectre.Console colour such as <c>#ff5faf</c> or <c>red</c>.</summary>
    public string Accent { get; init; } = "#ff5faf";

    /// <summary>The colour of reasoning, steps, tool calls and their results.</summary>
    public string Muted { get; init; } = "#8a8a8a";

    /// <summary>The colour of the mark on a tool call that succeeded.</summary>
    public string Success { get; init; } = "#5faf5f";

    /// <summary>The colour of the mark on a tool call that failed or was cancelled.</summary>
    public string Failure { get; init; } = "#d75f5f";

    /// <summary>What an indented line starts with, such as a tool call under the answer.</summary>
    public string Indent { get; init; } = "  ";

    /// <summary>The most characters of tool arguments and of one result line that are drawn; the rest is cut with <c>…</c>.</summary>
    public int MaxLineLength { get; init; } = 160;

    /// <summary>The most lines of a tool's result that are drawn; the rest is counted.</summary>
    public int MaxResultLines { get; init; } = 10;

    /// <summary>The most lines of a tool call's arguments that the approval panel shows, such as the lines of a script; the rest is counted.</summary>
    public int MaxApprovalLines { get; init; } = 40;

    /// <summary>
    /// Whether the runs an agent starts through a tool, such as its sub-agents, are drawn: each one's start and end, its tool calls and
    /// a line of its reasoning, indented under the tool call and labelled with the agent's name. False draws only the outer run.
    /// </summary>
    public bool ShowNestedRuns { get; init; } = true;

    /// <summary>Strings, such as API keys, that are drawn as <c>[redacted]</c> wherever the console would write them.</summary>
    public IReadOnlyCollection<string> SensitiveValues { get; init; } = [];
}
