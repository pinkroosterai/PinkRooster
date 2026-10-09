using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Agents.Permissions;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.SubAgents;

/// <summary>Fluent builder for a <see cref="SubAgentToolCollection"/>: the models and tool sets a sub-agent can get, then <see cref="Build"/>.</summary>
/// <remarks>
/// The builder is mutable: every method changes it and returns it. At least one model is required. Mistakes in the lists, such as
/// two models with one name or a tool set without tools, fail at <see cref="Build"/> with a message that names what to change.
/// </remarks>
/// <example>
/// <code>
/// SubAgentToolCollection subAgents = new SubAgentToolCollectionBuilder()
///     .WithModel("small", "Searching, reading and summarising.", smallClient)
///     .WithModel("large", "Hard reasoning and review.", largeClient)
///     .WithToolSet("files", "Reading, searching and changing files.", new FileOperationsToolCollection(workspace))
///     .Build();
/// </code>
/// </example>
public sealed class SubAgentToolCollectionBuilder
{
    private readonly List<SubAgentModel> models = [];
    private readonly List<SubAgentToolSet> toolSets = [];
    private readonly List<Action<AgentBuilder>> subAgentCallbacks = [];
    private int maxResultCharacters = SubAgentToolCollection.DefaultMaxResultCharacters;
    private Func<FunctionCallContent, CancellationToken, Task<ApprovalAnswer>>? approveToolCall;
    private ILoggerFactory? loggerFactory;

    /// <summary>Adds a model a sub-agent can run on.</summary>
    /// <param name="name">The name the calling model picks it by, such as <c>fast</c>.</param>
    /// <param name="useWhen">When to pick it, in a line the calling model reads.</param>
    /// <param name="chatClient">The client that runs it.</param>
    public SubAgentToolCollectionBuilder WithModel(string name, string useWhen, IChatClient chatClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(useWhen);
        ArgumentNullException.ThrowIfNull(chatClient);
        models.Add(new SubAgentModel(name, useWhen, chatClient));
        return this;
    }

    /// <summary>Adds a tool set made of tool collections.</summary>
    /// <param name="name">The name the calling model picks it by, such as <c>files</c>.</param>
    /// <param name="useWhen">When to pick it, in a line the calling model reads.</param>
    /// <param name="collections">The collections of the set: one, several, or a list of them. An instance is shared by every sub-agent that gets the set.</param>
    public SubAgentToolCollectionBuilder WithToolSet(string name, string useWhen, params IEnumerable<ToolCollection> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        return WithToolSet(new SubAgentToolSet(name, useWhen) { Collections = [.. collections] });
    }

    /// <summary>Adds a tool set made of plain tools, such as another agent's <c>AsAIFunction()</c>.</summary>
    /// <param name="name">The name the calling model picks it by.</param>
    /// <param name="useWhen">When to pick it, in a line the calling model reads.</param>
    /// <param name="tools">The tools of the set.</param>
    public SubAgentToolCollectionBuilder WithToolSet(string name, string useWhen, IEnumerable<AITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return WithToolSet(new SubAgentToolSet(name, useWhen) { Tools = [.. tools] });
    }

    /// <summary>Adds a tool set as given, for one that holds both collections and plain tools.</summary>
    public SubAgentToolCollectionBuilder WithToolSet(SubAgentToolSet toolSet)
    {
        ArgumentNullException.ThrowIfNull(toolSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolSet.Name, nameof(toolSet));
        ArgumentException.ThrowIfNullOrWhiteSpace(toolSet.UseWhen, nameof(toolSet));
        toolSets.Add(toolSet);
        return this;
    }

    /// <summary>Sets the most characters one result may hold; a longer answer is cut in the middle. Without it the limit is <see cref="SubAgentToolCollection.DefaultMaxResultCharacters"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxResultCharacters"/> is below 1.</exception>
    public SubAgentToolCollectionBuilder WithMaxResultCharacters(int maxResultCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResultCharacters, 1);
        this.maxResultCharacters = maxResultCharacters;
        return this;
    }

    /// <summary>Adds a callback that runs last on every sub-agent's builder, for defaults, a logger factory or tool-loop limits.</summary>
    /// <remarks>Callbacks run in the order they were added and may overwrite what the collection set.</remarks>
    public SubAgentToolCollectionBuilder ConfigureSubAgents(Action<AgentBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        subAgentCallbacks.Add(configure);
        return this;
    }

    /// <summary>
    /// Sets who answers when a sub-agent calls a tool that needs approval; true lets the call run. A console's
    /// <c>ConfirmToolCallAsync</c> fits as it is. Without it every such call is rejected.
    /// </summary>
    public SubAgentToolCollectionBuilder ApproveToolCallsWith(Func<FunctionCallContent, CancellationToken, Task<bool>> approveToolCall)
    {
        ArgumentNullException.ThrowIfNull(approveToolCall);
        return ApproveToolCallsWith(async (call, cancellationToken) => new ApprovalAnswer(await approveToolCall(call, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    /// Sets who answers when a sub-agent calls a tool that needs approval, for a callback that can say why it refuses: the sub-agent
    /// reads the reason as the tool's answer. A <see cref="PermissionPolicy"/>'s <see cref="PermissionPolicy.AnswerAsync"/> fits as it
    /// is, so an agent and its sub-agents are answered by one policy.
    /// </summary>
    public SubAgentToolCollectionBuilder ApproveToolCallsWith(Func<FunctionCallContent, CancellationToken, Task<ApprovalAnswer>> answerToolCall)
    {
        approveToolCall = answerToolCall ?? throw new ArgumentNullException(nameof(answerToolCall));
        return this;
    }

    /// <summary>
    /// Sets the logger factory every sub-agent gets, and where a sub-agent that could not be built or failed is logged. Without it such a
    /// failure reaches only the calling model, as an error it reads.
    /// </summary>
    public SubAgentToolCollectionBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        return this;
    }

    /// <summary>Builds a new collection from the builder's current settings. Can be called more than once; each collection is independent.</summary>
    /// <exception cref="ArgumentException">
    /// No model was added, a tool set holds no tool, or two models or two tool sets share a name, ignoring case.
    /// </exception>
    public SubAgentToolCollection Build()
    {
        Action<AgentBuilder>[] callbacks = [.. subAgentCallbacks];
        Action<AgentBuilder>? configure = callbacks.Length == 0 ? null : builder =>
        {
            foreach (Action<AgentBuilder> callback in callbacks)
            {
                callback(builder);
            }
        };
        return new SubAgentToolCollection(models, toolSets, maxResultCharacters, configure, approveToolCall, loggerFactory);
    }
}
