using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tools;

/// <summary>Turns the <see cref="ToolAttribute"/> methods and the context of an agent class into a tool collection, so they follow the rules of any other.</summary>
internal static class AgentOwnTools
{
    /// <summary>The collection for <paramref name="agent"/>, or null when it has no tool methods and no context of its own.</summary>
    /// <exception cref="ArgumentException">Two tool methods share a name, ignoring case.</exception>
    /// <exception cref="InvalidOperationException">A method marked with <see cref="ToolAttribute"/> is not public.</exception>
    public static ExternalToolCollection? Create(DeclaredAgent agent)
    {
        Type type = agent.GetType();
        IReadOnlyList<AIFunction> functions = ToolCollection.CreateTools(agent);

        bool hasContext = agent.OverridesContext;
        if (functions.Count == 0 && !hasContext)
        {
            return null;
        }

        ExternalToolCollection collection = new(type.Name, functions);
        return hasContext ? collection.WithContext(agent.ReadContextAsync) : collection;
    }
}
