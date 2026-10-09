
namespace PinkRooster.Agents.Briefs;

/// <summary>
/// The prompt sections of an agent as one value: role, objective, background, instructions, constraints, output format, examples, or the
/// whole system prompt. Give one to <see cref="AgentBuilder.WithBrief"/> to apply the same sections to several builders.
/// </summary>
/// <remarks>
/// Every part is optional and the rules are those of the builder's section methods: no blank text, and <c>systemPrompt</c> sets the whole
/// prompt instead of the sections, so it cannot be in the same brief as one.
/// </remarks>
public sealed class AgentBrief
{
    private const string CodeSource = "code";

    /// <summary>Makes a brief: no blank text, and <paramref name="systemPrompt"/> not together with a section.</summary>
    /// <remarks>Every parameter is optional; <c>new AgentBrief()</c> is an empty brief. <see cref="Source"/> is "code".</remarks>
    /// <param name="role">Who the agent is.</param>
    /// <param name="objective">What success looks like.</param>
    /// <param name="background">Background the agent should know.</param>
    /// <param name="instructions">The instructions, in order.</param>
    /// <param name="constraints">The constraints, in order.</param>
    /// <param name="outputFormat">The shape the response must take.</param>
    /// <param name="examples">The input and output examples, in order.</param>
    /// <param name="systemPrompt">The whole system prompt, instead of the sections.</param>
    /// <exception cref="ArgumentException">A text, an instruction, a constraint or an example part is blank, or <paramref name="systemPrompt"/> is set together with a section; the message names the parameter and the fix.</exception>
    public AgentBrief(
        string? role = null,
        string? objective = null,
        string? background = null,
        IEnumerable<string>? instructions = null,
        IEnumerable<string>? constraints = null,
        string? outputFormat = null,
        IEnumerable<AgentExample>? examples = null,
        string? systemPrompt = null)
        : this(
            CodeSource,
            Text(role, nameof(role)),
            Text(objective, nameof(objective)),
            Text(background, nameof(background)),
            Text(outputFormat, nameof(outputFormat)),
            Text(systemPrompt, nameof(systemPrompt)),
            Lines(instructions, nameof(instructions)),
            Lines(constraints, nameof(constraints)),
            CheckExamples(examples, nameof(examples)))
    {
        if (SystemPrompt is not null && HasSections)
        {
            throw new ArgumentException(
                $"{nameof(systemPrompt)} sets the whole prompt, so it cannot be combined with sections in the same brief; remove {nameof(systemPrompt)} or the sections.", nameof(systemPrompt));
        }
    }

    internal AgentBrief(
        string source,
        string? role,
        string? objective,
        string? background,
        string? outputFormat,
        string? systemPrompt,
        IReadOnlyList<string> instructions,
        IReadOnlyList<string> constraints,
        IReadOnlyList<AgentExample> examples)
    {
        Source = source;
        Role = role;
        Objective = objective;
        Background = background;
        OutputFormat = outputFormat;
        SystemPrompt = systemPrompt;
        Instructions = instructions;
        Constraints = constraints;
        Examples = examples;
    }

    /// <summary>Where the brief came from: "code", or the name of the agent class whose attributes made it. Error messages name it.</summary>
    public string Source { get; }

    /// <summary>Who the agent is, or null when the brief does not say.</summary>
    public string? Role { get; }

    /// <summary>What success looks like for the agent, or null.</summary>
    public string? Objective { get; }

    /// <summary>Background the agent should know, or null.</summary>
    public string? Background { get; }

    /// <summary>The shape the agent's response must take, or null.</summary>
    public string? OutputFormat { get; }

    /// <summary>The whole system prompt, sent as given, or null. Never set together with a section.</summary>
    public string? SystemPrompt { get; }

    /// <summary>The instructions, in order.</summary>
    public IReadOnlyList<string> Instructions { get; }

    /// <summary>The constraints, in order.</summary>
    public IReadOnlyList<string> Constraints { get; }

    /// <summary>The input and output examples, in order.</summary>
    public IReadOnlyList<AgentExample> Examples { get; }

    /// <summary>Whether any section is set; the whole <see cref="SystemPrompt"/> does not count. The one place that lists the sections.</summary>
    internal bool HasSections => Role is not null || Objective is not null || Background is not null || OutputFormat is not null
        || Instructions.Count > 0 || Constraints.Count > 0 || Examples.Count > 0;

    private static string? Text(string? value, string parameter) =>
        value is not null && string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameter} is blank; pass the text, or leave {parameter} out.", parameter)
            : value;

    private static string[] Lines(IEnumerable<string>? values, string parameter)
    {
        string[] lines = [.. values ?? []];
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                throw new ArgumentException($"{parameter}[{i}] is blank; remove it or write the line.", parameter);
            }
        }
        return lines;
    }

    private static AgentExample[] CheckExamples(IEnumerable<AgentExample>? values, string parameter)
    {
        AgentExample[] examples = [.. values ?? []];
        for (int i = 0; i < examples.Length; i++)
        {
            if (examples[i] is null || string.IsNullOrWhiteSpace(examples[i].Input) || string.IsNullOrWhiteSpace(examples[i].Output))
            {
                throw new ArgumentException($"{parameter}[{i}] needs a non-blank Input and Output; write both, or remove the example.", parameter);
            }
        }
        return examples;
    }
}
