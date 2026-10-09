using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.Shared;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.SubAgents;

/// <summary>
/// Exposes a <c>RunSubAgent</c> tool that lets an agent hand a task to a sub-agent it briefs itself, on a model and with tool sets
/// it picks from the lists the host gives here.
/// </summary>
/// <remarks>
/// Every call builds a new sub-agent with <see cref="AgentBuilder"/>, runs it once and returns its answer; nothing is kept between
/// calls, and the sub-agent never gets this collection, so it cannot start sub-agents of its own. Its events reach the calling
/// agent's <see cref="AgentBuilder.OnEvent(Action{Eventing.AgentEvent})"/> handlers with <c>ParentRunId</c> set.
/// </remarks>
public sealed class SubAgentToolCollection : ToolCollection
{
    /// <summary>The most characters one result holds unless the constructor is given another limit.</summary>
    public const int DefaultMaxResultCharacters = 20_000;

    private readonly SubAgentModel[] models;
    private readonly SubAgentToolSet[] toolSets;
    private readonly int maxResultCharacters;
    private readonly Action<AgentBuilder>? configure;
    private readonly Func<FunctionCallContent, CancellationToken, Task<ApprovalAnswer>>? approveToolCall;
    private readonly ILoggerFactory? loggerFactory;
    private readonly ILogger? logger;
    // One approval question at a time, also when several sub-agents run at once.
    private readonly SemaphoreSlim approvalGate = new(1, 1);

    /// <summary>Creates a collection whose sub-agents run on one of <paramref name="models"/> and have no tools.</summary>
    /// <remarks>For tool sets, a result limit, a callback on each sub-agent's builder or approvals use <see cref="SubAgentToolCollectionBuilder"/>.</remarks>
    /// <param name="models">The models a sub-agent can run on; at least one, with names that differ ignoring case.</param>
    /// <exception cref="ArgumentException">There is no model, a name or <c>UseWhen</c> is blank, a client is null, or two models share a name.</exception>
    public SubAgentToolCollection(IEnumerable<SubAgentModel> models) : this(models, toolSets: null)
    {
    }

    /// <param name="models">The models a sub-agent can run on; at least one, with names that differ ignoring case.</param>
    /// <param name="toolSets">The tool sets the calling model can give a sub-agent. Without any, sub-agents have no tools.</param>
    /// <param name="maxResultCharacters">The most characters one result may hold; a longer answer is cut in the middle.</param>
    /// <param name="configure">Runs last on every sub-agent's builder, for defaults, a logger factory or tool-loop limits. It may overwrite what the collection set.</param>
    /// <param name="approveToolCall">
    /// Asked about each call a sub-agent makes to a tool that needs approval, one at a time. Without it every such call is rejected
    /// and the sub-agent goes on without that tool.
    /// </param>
    /// <param name="loggerFactory">Given to every sub-agent, and where a sub-agent that could not be built or failed is logged; the calling model is told either way.</param>
    /// <exception cref="ArgumentException">
    /// There is no model, a name or <c>UseWhen</c> is blank, a client is null, a tool set holds no tool, or two models or two tool sets share a name.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxResultCharacters"/> is below 1.</exception>
    internal SubAgentToolCollection(
        IEnumerable<SubAgentModel> models,
        IEnumerable<SubAgentToolSet>? toolSets = null,
        int maxResultCharacters = DefaultMaxResultCharacters,
        Action<AgentBuilder>? configure = null,
        Func<FunctionCallContent, CancellationToken, Task<ApprovalAnswer>>? approveToolCall = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResultCharacters, 1);

        this.models = [.. models];
        this.toolSets = [.. toolSets ?? []];
        this.maxResultCharacters = maxResultCharacters;
        this.configure = configure;
        this.approveToolCall = approveToolCall;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory?.CreateLogger<SubAgentToolCollection>();

        if (this.models.Length == 0)
        {
            throw new ArgumentException("A sub-agent needs a model to run on; pass at least one SubAgentModel.", nameof(models));
        }
        foreach (SubAgentModel model in this.models)
        {
            if (model is null || string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.UseWhen))
            {
                throw new ArgumentException("Every SubAgentModel needs a Name and a UseWhen line; the calling model picks a model by them.", nameof(models));
            }
            if (model.ChatClient is null)
            {
                throw new ArgumentException($"The model '{model.Name}' has no ChatClient; give it the client that runs it.", nameof(models));
            }
        }
        foreach (SubAgentToolSet toolSet in this.toolSets)
        {
            if (toolSet is null || string.IsNullOrWhiteSpace(toolSet.Name) || string.IsNullOrWhiteSpace(toolSet.UseWhen))
            {
                throw new ArgumentException("Every SubAgentToolSet needs a Name and a UseWhen line; the calling model picks tool sets by them.", nameof(toolSets));
            }
            if (toolSet.Collections is null || toolSet.Tools is null || toolSet.Collections.Count + toolSet.Tools.Count == 0
                || toolSet.Collections.Any(collection => collection is null) || toolSet.Tools.Any(tool => tool is null))
            {
                throw new ArgumentException($"The tool set '{toolSet.Name}' holds no tool, or a null one; set its Collections or its Tools.", nameof(toolSets));
            }
        }
        ThrowIfNamesRepeat(this.models.Select(model => model.Name), "models", nameof(models));
        ThrowIfNamesRepeat(this.toolSets.Select(toolSet => toolSet.Name), "tool sets", nameof(toolSets));

        List<string> lines = ["RunSubAgent runs a sub-agent on one of these models:", .. this.models.Select(model => $"- {model.Name}: {model.UseWhen}")];
        if (this.toolSets.Length > 0)
        {
            lines.Add("A sub-agent has only the tools of the tool sets you name:");
            lines.AddRange(this.toolSets.Select(toolSet => $"- {toolSet.Name}: {toolSet.UseWhen}"));
        }
        AddInstruction(string.Join("\n", lines));
    }

    /// <summary>The <c>RunSubAgent</c> tool; the <see cref="ToolAttribute" /> description is what the model reads.</summary>
    [Tool("RunSubAgent",
        "Runs a sub-agent on one task and returns its final answer. Use it for a self-contained piece of work whose steps you do not need to see, " +
        "such as searching many files or researching a question, or to give work to a model that suits it better; do small tasks yourself. " +
        "The sub-agent starts with no memory: it sees nothing of this conversation, only the role, instructions, output format and prompt you write, " +
        "so put in everything it needs: the goal, what to look at and what to leave alone. Each call is a new sub-agent, so a later call cannot " +
        "continue an earlier one. Returns the answer as text, cut in the middle when it is very long, so ask for a short result. " +
        "A call that could not run or failed returns 'Error: ...' with what to change.",
        Kind = ToolKind.State)]
    public async Task<string> RunSubAgent(
        [Description("The name of the model to run on, one of the models listed in your instructions.")] string modelName,
        [Description("A short name for this sub-agent, such as 'TestFinder'. It labels the sub-agent's activity for the user.")] string subAgentName,
        [Description("Who the sub-agent is, such as 'You are a code reviewer for C# libraries.'")] string role,
        [Description("The task, with everything needed to do it: the goal, the facts you already have, what to look at and what to leave alone.")] string prompt,
        [Description("Optional rules for how to work, one per item, such as 'Read a file before you judge it.'")] string[]? instructions = null,
        [Description("Optional shape of the answer, such as 'At most 10 bullet points, file paths with line numbers.'")] string? outputFormat = null,
        [Description("Optional names of the tool sets to give the sub-agent, from the tool sets listed in your instructions. Leave it out for a sub-agent without tools.")] string[]? toolSets = null,
        CancellationToken cancellationToken = default)
    {
        string? error = Validate(modelName, subAgentName, role, prompt, instructions, toolSets, out SubAgentModel model, out SubAgentToolSet[] chosen);
        if (error is not null)
        {
            return error;
        }

        AIAgent agent;
        try
        {
            AgentBuilder builder = model.ChatClient.CreateAgent().WithName(subAgentName).WithRole(role);
            if (instructions is { Length: > 0 })
            {
                builder.WithInstructions(instructions);
            }
            if (!string.IsNullOrWhiteSpace(outputFormat))
            {
                builder.WithOutputFormat(outputFormat);
            }
            // A collection or tool that sits in two of the named sets goes in once.
            builder.WithTools(chosen.SelectMany(toolSet => toolSet.Collections).Distinct());
            builder.WithTools(chosen.SelectMany(toolSet => toolSet.Tools).Distinct());
            if (loggerFactory is not null)
            {
                builder.WithLoggerFactory(loggerFactory);
            }
            configure?.Invoke(builder);
            agent = builder.Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The calling model reads the message below; the log keeps the exception for the host.
            logger?.LogError(ex, "The sub-agent {SubAgent} could not be built.", subAgentName);
            string named = chosen.Length == 0 ? "" : $" with the tool sets {Quote(chosen.Select(toolSet => toolSet.Name))}";
            return $"Error: The sub-agent '{subAgentName}' could not be built{named}: {ex.Message}";
        }

        string text;
        try
        {
            // An approval ends the run; the answers go back on the same session and the run goes on.
            AgentResponse response = await AgentApprovalExtensions.RunWithApprovalsAsync(
                agent, [new ChatMessage(ChatRole.User, prompt)], session: null, AnswerAsync, options: null, cancellationToken).ConfigureAwait(false);
            text = response.Text;
        }
        // A cancellation the caller asked for is theirs to see; anything else the calling model can act on.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogError(ex, "The sub-agent {SubAgent} failed on model {Model}.", subAgentName, model.Name);
            return $"Error: The sub-agent '{subAgentName}' failed: {ex.Message} Its work so far is lost; call again, pick another model, or do the task yourself.";
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return $"Error: The sub-agent '{subAgentName}' gave no answer. Call again with a prompt that says what to report, or do the task yourself.";
        }
        return TextCut.InTheMiddle(text, maxResultCharacters);
    }

    private string? Validate(string modelName, string subAgentName, string role, string prompt, string[]? instructions, string[]? toolSetNames,
        out SubAgentModel model, out SubAgentToolSet[] chosen)
    {
        chosen = [];
        model = models.FirstOrDefault(candidate => string.Equals(candidate.Name, modelName, StringComparison.OrdinalIgnoreCase))!;
        if (model is null)
        {
            return $"Error: Unknown model '{modelName}'. The models are: {Quote(models.Select(candidate => candidate.Name))}.";
        }
        if (string.IsNullOrWhiteSpace(subAgentName))
        {
            return "Error: subAgentName is blank. Give the sub-agent a short name.";
        }
        if (string.IsNullOrWhiteSpace(role))
        {
            return "Error: role is blank. Say who the sub-agent is.";
        }
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Error: prompt is blank. Write the task the sub-agent must do.";
        }
        if (instructions is not null && instructions.Any(string.IsNullOrWhiteSpace))
        {
            return "Error: instructions holds a blank item. Remove it, or leave instructions out.";
        }

        List<SubAgentToolSet> found = [];
        foreach (string name in toolSetNames ?? [])
        {
            SubAgentToolSet? toolSet = toolSets.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (toolSet is null)
            {
                return toolSets.Length == 0
                    ? $"Error: Unknown tool set '{name}'. There are no tool sets; leave toolSets out."
                    : $"Error: Unknown tool set '{name}'. The tool sets are: {Quote(toolSets.Select(candidate => candidate.Name))}.";
            }
            if (!found.Contains(toolSet))
            {
                found.Add(toolSet);
            }
        }
        chosen = [.. found];
        return null;
    }

    private async Task<AIContent> AnswerAsync(ToolApprovalRequestContent request, CancellationToken cancellationToken)
    {
        if (approveToolCall is null || request.ToolCall is not FunctionCallContent)
        {
            return request.CreateResponse(false, "Sub-agents cannot get approval for this tool here. Go on without it.");
        }

        await approvalGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await AgentApprovalExtensions.AnswerAsync(request, approveToolCall, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            approvalGate.Release();
        }
    }

    private static string Quote(IEnumerable<string> names) => string.Join(", ", names.Select(name => $"'{name}'"));

    private static void ThrowIfNamesRepeat(IEnumerable<string> names, string what, string paramName)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (!seen.Add(name))
            {
                throw new ArgumentException($"Two {what} are named '{name}'. Names must differ, ignoring case; rename one.", paramName);
            }
        }
    }
}
