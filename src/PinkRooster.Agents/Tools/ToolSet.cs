using Microsoft.Extensions.AI;
using PinkRooster.Agents.Background;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tools;

/// <summary>A builder's tools, tool collections and the names that need approval, and the rules that turn them into the tool list an agent gets.</summary>
internal sealed class ToolSet
{
    private readonly List<AITool> tools;
    private readonly List<ToolCollection> collections;
    private readonly List<string> approvalRequired;
    private readonly List<string> backgroundAllowed;

    public ToolSet()
    {
        tools = [];
        collections = [];
        approvalRequired = [];
        backgroundAllowed = [];
    }

    private ToolSet(ToolSet source)
    {
        tools = [.. source.tools];
        collections = [.. source.collections];
        approvalRequired = [.. source.approvalRequired];
        backgroundAllowed = [.. source.backgroundAllowed];
    }

    public IReadOnlyList<ToolCollection> Collections => collections;

    public void AddTool(AITool tool) => tools.Add(tool);

    public void AddTools(IEnumerable<AITool> added) => tools.AddRange(added);

    public void AddCollections(IEnumerable<ToolCollection> added) => collections.AddRange(added);

    public void RequireApproval(IEnumerable<string> toolNames) => approvalRequired.AddRange(toolNames);

    public void AllowBackground(IEnumerable<string> toolNames) => backgroundAllowed.AddRange(toolNames);

    /// <summary>Whether any tool was allowed in the background.</summary>
    public bool HasBackground => backgroundAllowed.Count > 0;

    public ToolSet Clone() => new(this);

    /// <summary>The tools, then the collections' tools, with the background parameter on the tools allowed it and the approval wrappers on the tools that need them.</summary>
    /// <exception cref="InvalidOperationException">
    /// Two tools share a name; an approval or background name matches no tool, or names a tool that is not a function; or a tool allowed
    /// in the background already has a <c>runInBackground</c> parameter.
    /// </exception>
    public List<AITool> Build()
    {
        List<(AITool Tool, string Source, ToolCollection? Collection)> all =
        [
            .. tools.Select(tool => (tool, "WithTool", (ToolCollection?)null)),
            .. collections.SelectMany(collection => collection.GetAIFunctions().Select(function => ((AITool)function, $"collection '{collection.DisplayName}'", (ToolCollection?)collection)))
        ];

        Dictionary<string, (string Source, ToolCollection? Collection)> sources = new(StringComparer.OrdinalIgnoreCase);
        foreach ((AITool tool, string source, ToolCollection? collection) in all)
        {
            if (sources.TryGetValue(tool.Name, out (string Source, ToolCollection? Collection) first))
            {
                string[] hints = [.. new[] { first.Collection, collection }.Select(side => side?.RenameHint(tool.Name)).OfType<string>()];
                throw new InvalidOperationException(
                    $"Duplicate tool name '{tool.Name}', from {first.Source} and from {source}. Tool names must be unique, ignoring case; " +
                    (hints.Length > 0
                        ? string.Join("; or ", hints) + "."
                        : "rename one with WithTool(method, name) or [Tool(\"<name>\", ...)], or give an added tool a different name before adding it."));
            }
            sources.Add(tool.Name, (source, collection));
        }

        HashSet<string> guarded = new(approvalRequired, StringComparer.OrdinalIgnoreCase);
        foreach (string toolName in guarded)
        {
            if (!sources.ContainsKey(toolName))
            {
                string known = sources.Count == 0 ? "none" : string.Join(", ", sources.Keys.Select(key => $"'{key}'"));
                throw new InvalidOperationException($"{nameof(AgentBuilder.RequireApproval)} names '{toolName}', but the builder has no such tool. Its tools are: {known}.");
            }
        }

        HashSet<string> background = new(backgroundAllowed, StringComparer.OrdinalIgnoreCase);
        foreach (string toolName in background)
        {
            if (!sources.ContainsKey(toolName))
            {
                string known = sources.Count == 0 ? "none" : string.Join(", ", sources.Keys.Select(key => $"'{key}'"));
                throw new InvalidOperationException($"{nameof(AgentBuilder.AllowBackground)} names '{toolName}', but the builder has no such tool. Its tools are: {known}.");
            }
        }

        List<AITool> built = [];
        foreach ((AITool tool, _, _) in all)
        {
            AITool next = tool;
            if (background.Contains(tool.Name))
            {
                if (tool is not AIFunction function)
                {
                    throw new InvalidOperationException($"{nameof(AgentBuilder.AllowBackground)} names '{tool.Name}', which is a {tool.GetType().Name}, not a function; only functions can run in the background.");
                }
                if (BackgroundFunction.HasParameter(function))
                {
                    throw new InvalidOperationException(
                        $"{nameof(AgentBuilder.AllowBackground)} names '{tool.Name}', which already has a parameter named '{BackgroundFunction.ParameterName}', the name the builder adds. " +
                        $"Rename that parameter, or remove '{tool.Name}' from {nameof(AgentBuilder.AllowBackground)}.");
                }
                next = new BackgroundFunction(function);
            }

            // The tool loop recognises approval by this type, so it goes on last, also around a tool its collection already marked.
            bool needsApproval = guarded.Contains(tool.Name) || tool is ApprovalRequiredAIFunction;
            if (!needsApproval || next is ApprovalRequiredAIFunction)
            {
                built.Add(next);
            }
            else if (next is AIFunction function)
            {
                built.Add(new ApprovalRequiredAIFunction(function));
            }
            else
            {
                throw new InvalidOperationException($"{nameof(AgentBuilder.RequireApproval)} names '{tool.Name}', which is a {tool.GetType().Name}, not a function; only functions can require approval.");
            }
        }
        return built;
    }
}
