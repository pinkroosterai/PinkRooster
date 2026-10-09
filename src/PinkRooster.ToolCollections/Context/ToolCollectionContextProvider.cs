using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace PinkRooster.ToolCollections.Context;

/// <summary>
/// Gives tool collections to any MAF agent the agent builder did not make, such as a <c>HarnessAgent</c> or a hand-built
/// <see cref="ChatClientAgent"/>: their tools, their instructions and constraints, and their current context.
/// </summary>
/// <remarks>
/// Add it to the agent's <c>AIContextProviders</c>. It runs once per agent run, so context is fresh per run rather than per
/// model call inside the tool loop; for per-call context, put a <see cref="ToolCollectionChatClient"/> in the agent's client
/// instead. Context goes in as instructions, not messages, so it is never stored in the session's history. Do not combine it
/// with a <see cref="ToolCollectionChatClient"/> for the same collections, or the context arrives twice.
/// </remarks>
public sealed class ToolCollectionContextProvider : AIContextProvider
{
    private readonly ToolCollection[] collections;
    private readonly ILogger? logger;
    private readonly bool contextOnly;

    /// <param name="collections">The collections to give the agent, in this order.</param>
    /// <exception cref="InvalidOperationException">Two collections expose a tool with the same name, ignoring case.</exception>
    public ToolCollectionContextProvider(params IEnumerable<ToolCollection> collections) : this(collections, logger: null)
    {
    }

    /// <param name="collections">The collections to give the agent, in this order.</param>
    /// <param name="logger">Where a failing collection context is logged. With or without one the collection is left out of that run's context and the run goes on; without one the failure is invisible.</param>
    /// <exception cref="InvalidOperationException">Two collections expose a tool with the same name, ignoring case.</exception>
    public ToolCollectionContextProvider(IEnumerable<ToolCollection> collections, ILogger? logger) : this(collections, logger, contextOnly: false)
    {
    }

    private ToolCollectionContextProvider(IEnumerable<ToolCollection> collections, ILogger? logger, bool contextOnly)
    {
        this.collections = ToolCollectionContext.Copy(collections, nameof(collections));
        ToolCollectionContext.ThrowOnDuplicateToolNames(this.collections);
        this.logger = logger;
        this.contextOnly = contextOnly;
    }

    /// <summary>
    /// A provider that sends only the collections' current context, once per run, for an agent that already carries their tools and
    /// their standing text, such as one made from the agent builder's <c>BuildOptions()</c>.
    /// </summary>
    /// <param name="collections">The collections whose context is sent, in this order.</param>
    /// <param name="logger">Where a failing collection context is logged; with or without one the collection is left out and the run goes on.</param>
    /// <exception cref="InvalidOperationException">Two collections expose a tool with the same name, ignoring case.</exception>
    public static ToolCollectionContextProvider ForContextOnly(IEnumerable<ToolCollection> collections, ILogger? logger = null) => new(collections, logger, contextOnly: true);

    /// <inheritdoc />
    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        string? current = await ToolCollectionContext.BuildAsync(collections, logger, cancellationToken).ConfigureAwait(false);
        if (contextOnly)
        {
            return new AIContext { Instructions = current };
        }

        StringBuilder instructions = new();
        AppendList(instructions, "Instructions", collections.SelectMany(collection => collection.Instructions));
        AppendList(instructions, "Constraints", collections.SelectMany(collection => collection.Constraints));
        if (current is not null)
        {
            instructions.Append(current);
        }

        string text = instructions.ToString().TrimEnd();
        return new AIContext
        {
            Instructions = text.Length == 0 ? null : text,
            Tools = [.. collections.SelectMany(collection => collection.GetAIFunctions())]
        };
    }

    private static void AppendList(StringBuilder text, string heading, IEnumerable<string> items)
    {
        string[] distinct = [.. items.Distinct(StringComparer.Ordinal)];
        if (distinct.Length == 0)
        {
            return;
        }
        text.AppendLine($"# {heading}");
        foreach (string item in distinct)
        {
            text.AppendLine($"- {item}");
        }
        text.AppendLine();
    }
}
