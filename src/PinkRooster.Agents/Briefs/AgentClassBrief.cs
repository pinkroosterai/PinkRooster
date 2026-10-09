using PinkRooster.Agents.Shared;

namespace PinkRooster.Agents.Briefs;

/// <summary>Reads the brief attributes of an agent class into one <see cref="AgentBrief"/> whose source is the class's name.</summary>
internal static class AgentClassBrief
{
    /// <summary>The brief the class and its base types declare, or null when none declares anything.</summary>
    /// <exception cref="InvalidOperationException">An attribute holds blank text; the message names the class, the attribute and the fix.</exception>
    public static AgentBrief? Read(Type type)
    {
        // Base types first, so a single-text section on a derived class replaces the base's and the lists read general before specific.
        List<Type> chain = TypeChain.BaseFirst(type);

        string? role = Single<AgentRoleAttribute>(type, chain, attribute => attribute.Lines);
        string? objective = Single<AgentObjectiveAttribute>(type, chain, attribute => attribute.Lines);
        string? background = Single<AgentBackgroundAttribute>(type, chain, attribute => attribute.Lines);
        string? outputFormat = Single<AgentOutputFormatAttribute>(type, chain, attribute => attribute.Lines);
        IReadOnlyList<string> instructions = Many<AgentInstructionAttribute>(type, chain, attribute => attribute.Instructions);
        IReadOnlyList<string> constraints = Many<AgentConstraintAttribute>(type, chain, attribute => attribute.Constraints);
        IReadOnlyList<AgentExample> examples = Examples(type, chain);

        AgentBrief brief = new(type.Name, role, objective, background, outputFormat, systemPrompt: null, instructions, constraints, examples);
        return brief.HasSections ? brief : null;
    }

    private static string? Single<TAttribute>(Type type, List<Type> chain, Func<TAttribute, string[]> lines) where TAttribute : Attribute
    {
        string? found = null;
        foreach (Type current in chain)
        {
            if (current.GetCustomAttributes(typeof(TAttribute), inherit: false).Cast<TAttribute>().FirstOrDefault() is TAttribute attribute)
            {
                string[] text = lines(attribute);
                if (text.Length == 0 || text.Any(string.IsNullOrWhiteSpace))
                {
                    throw TypeChain.Blank(typeof(TAttribute), type, current);
                }
                found = string.Join('\n', text);
            }
        }
        return found;
    }

    private static List<string> Many<TAttribute>(Type type, List<Type> chain, Func<TAttribute, string[]> lines) where TAttribute : Attribute
    {
        List<string> items = [];
        foreach (Type current in chain)
        {
            foreach (TAttribute attribute in current.GetCustomAttributes(typeof(TAttribute), inherit: false).Cast<TAttribute>())
            {
                string[] text = lines(attribute);
                if (text.Length == 0 || text.Any(string.IsNullOrWhiteSpace))
                {
                    throw TypeChain.Blank(typeof(TAttribute), type, current);
                }
                items.AddRange(text);
            }
        }
        return items;
    }

    private static List<AgentExample> Examples(Type type, List<Type> chain)
    {
        List<AgentExample> items = [];
        foreach (Type current in chain)
        {
            foreach (AgentExampleAttribute attribute in current.GetCustomAttributes(typeof(AgentExampleAttribute), inherit: false).Cast<AgentExampleAttribute>())
            {
                if (string.IsNullOrWhiteSpace(attribute.Input) || string.IsNullOrWhiteSpace(attribute.Output))
                {
                    throw TypeChain.Blank(typeof(AgentExampleAttribute), type, current);
                }
                items.Add(new AgentExample(attribute.Input, attribute.Output));
            }
        }
        return items;
    }
}
