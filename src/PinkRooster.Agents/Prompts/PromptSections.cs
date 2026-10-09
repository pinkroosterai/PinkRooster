using System.Text;
using PinkRooster.Agents.Briefs;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Prompts;

/// <summary>The text an agent's system prompt is composed from, either as named sections or as one raw prompt, and the layout the sections are sent in.</summary>
internal sealed class PromptSections
{
    public string? Role { get; set; }

    public string? Objective { get; set; }

    public string? Background { get; set; }

    public string? OutputFormat { get; set; }

    /// <summary>The whole prompt, sent as given; cannot be combined with any section.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>The message of the error for a missing role, when the caller has a fix of its own to name; null for the builder's.</summary>
    public string? MissingRoleMessage { get; set; }

    public List<string> Instructions { get; } = [];

    public List<string> Constraints { get; } = [];

    public List<AgentExample> Examples { get; } = [];

    /// <summary>The sources of the briefs applied so far, in order, for the error when a brief's text clashes with text from another call.</summary>
    public List<string> BriefSources { get; } = [];

    public PromptSections Clone()
    {
        PromptSections copy = new()
        {
            Role = Role,
            Objective = Objective,
            Background = Background,
            OutputFormat = OutputFormat,
            SystemPrompt = SystemPrompt,
            MissingRoleMessage = MissingRoleMessage
        };
        copy.Instructions.AddRange(Instructions);
        copy.Constraints.AddRange(Constraints);
        copy.Examples.AddRange(Examples);
        copy.BriefSources.AddRange(BriefSources);
        return copy;
    }

    /// <summary>Applies a brief as the section methods would: a single-value section the brief sets replaces the one before, and lists add to the ones before.</summary>
    public void Apply(AgentBrief brief)
    {
        Role = brief.Role ?? Role;
        Objective = brief.Objective ?? Objective;
        Background = brief.Background ?? Background;
        OutputFormat = brief.OutputFormat ?? OutputFormat;
        SystemPrompt = brief.SystemPrompt ?? SystemPrompt;
        Instructions.AddRange(brief.Instructions);
        Constraints.AddRange(brief.Constraints);
        Examples.AddRange(brief.Examples);
        BriefSources.Add(brief.Source);
    }

    /// <summary>Composes the system prompt: the raw prompt as given, or the sections in a fixed order with the defaults and the collections' text in front of the own lines.</summary>
    /// <param name="defaults">The defaults to use, or null for <see cref="AgentDefaults.BuiltIn"/>.</param>
    /// <param name="collections">The tool collections whose instructions and constraints go in, in order.</param>
    /// <exception cref="InvalidOperationException">A raw prompt and a section are both set, or neither a raw prompt nor a role is.</exception>
    public string Compose(AgentDefaults? defaults, IReadOnlyList<ToolCollection> collections)
    {
        bool anySection = Role is not null || Objective is not null || Background is not null || OutputFormat is not null
            || Instructions.Count > 0 || Constraints.Count > 0 || Examples.Count > 0;

        if (SystemPrompt is not null)
        {
            if (anySection)
            {
                string briefs = BriefSources.Count == 0 ? "" : $" Briefs applied: {string.Join(", ", BriefSources.Select(source => $"'{source}'"))}.";
                throw new InvalidOperationException(
                    $"{nameof(AgentBuilder.WithSystemPrompt)} or a brief's systemPrompt cannot be combined with section methods such as {nameof(AgentBuilder.WithRole)} or " +
                    $"{nameof(AgentBuilder.WithInstruction)}, or with a brief's sections; use one or the other.{briefs}");
            }
            return SystemPrompt;
        }

        if (Role is null)
        {
            throw new InvalidOperationException(MissingRoleMessage
                ?? $"Call {nameof(AgentBuilder.WithRole)} before {nameof(AgentBuilder.Build)}, or set the whole prompt with {nameof(AgentBuilder.WithSystemPrompt)}.");
        }

        AgentDefaults effective = defaults ?? AgentDefaults.BuiltIn;
        StringBuilder prompt = new();
        AppendText(prompt, "Role", Role);
        AppendText(prompt, "Objective", Objective);
        AppendText(prompt, "Background", Background);
        AppendList(prompt, "Instructions", Combine(effective.Instructions, collections.Select(collection => collection.Instructions), Instructions));
        AppendList(prompt, "Constraints", Combine(effective.Constraints, collections.Select(collection => collection.Constraints), Constraints));
        AppendText(prompt, "Output Format", OutputFormat);
        if (Examples.Count > 0)
        {
            prompt.AppendLine("# Examples");
            for (int i = 0; i < Examples.Count; i++)
            {
                prompt.AppendLine($"Example {i + 1}:");
                prompt.AppendLine($"Input: {Examples[i].Input}");
                prompt.AppendLine($"Output: {Examples[i].Output}");
                prompt.AppendLine();
            }
        }
        return prompt.ToString().TrimEnd();
    }

    private static void AppendText(StringBuilder prompt, string heading, string? text)
    {
        if (text is null)
        {
            return;
        }
        prompt.AppendLine($"# {heading}");
        prompt.AppendLine(text);
        prompt.AppendLine();
    }

    // Defaults first, then collection text in the order the collections were added, then the builder's own; repeated text is sent once.
    private static List<string> Combine(IReadOnlyList<string> defaults, IEnumerable<IReadOnlyList<string>> collectionItems, List<string> ownItems) =>
        [.. defaults.Concat(collectionItems.SelectMany(items => items)).Concat(ownItems).Distinct(StringComparer.Ordinal)];

    // A later line of an item is indented under its bullet, so it reads as part of the item; a blank line stays blank.
    private static void AppendItem(StringBuilder prompt, string item)
    {
        if (!item.Contains('\n'))
        {
            prompt.AppendLine($"- {item}");
            return;
        }

        string[] lines = item.TrimEnd('\r', '\n').Split('\n');
        prompt.AppendLine($"- {lines[0].TrimEnd('\r')}");
        foreach (string line in lines.Skip(1).Select(line => line.TrimEnd('\r')))
        {
            prompt.AppendLine(line.Length == 0 ? "" : $"  {line}");
        }
    }

    private static void AppendList(StringBuilder prompt, string heading, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        prompt.AppendLine($"# {heading}");
        foreach (string item in items)
        {
            AppendItem(prompt, item);
        }
        prompt.AppendLine();
    }
}
