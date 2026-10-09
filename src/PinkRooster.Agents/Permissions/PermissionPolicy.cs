using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Permissions;

/// <summary>
/// Answers the tool calls that need approval from a mode and a list of allow rules, and asks the user only about what is left.
/// Give <see cref="AnswerAsync"/> to <c>RunWithApprovalsAsync</c> and to a sub-agent collection's <c>ApproveToolCallsWith</c>, so
/// an agent and its sub-agents are answered by one policy.
/// </summary>
/// <remarks>
/// <para>
/// The policy decides by what a tool declares it does, its <see cref="ToolKind"/>, so it must be told the tools: <see cref="WithTools(ToolCollection[])"/>.
/// A tool it was not told, or one that declares no kind, is never allowed by a mode: only by a rule that names it, or by the user.
/// </para>
/// <list type="table">
/// <listheader><term>Mode</term><description>read and state / edit / execute and no kind</description></listheader>
/// <item><term><see cref="PermissionMode.Ask"/></term><description>allowed / asks / asks</description></item>
/// <item><term><see cref="PermissionMode.AcceptEdits"/></term><description>allowed / allowed / asks</description></item>
/// <item><term><see cref="PermissionMode.Plan"/></term><description>allowed / refused / refused unless a rule given with <see cref="Allow"/> covers the call</description></item>
/// </list>
/// <para>
/// A rule is looked at before the user is asked. In <see cref="PermissionMode.Plan"/> only the rules given with <see cref="Allow"/>
/// count, never one the user added at a prompt, and never for a tool that edits.
/// </para>
/// <para>
/// The policy only answers calls that reach it: a tool that does not need approval runs without it. It is safe to use from several
/// runs at once; the user is asked one question at a time.
/// </para>
/// </remarks>
public sealed class PermissionPolicy
{
    private readonly Func<ApprovalQuestion, CancellationToken, Task<ApprovalChoice>> ask;
    private readonly object gate = new();
    private readonly SemaphoreSlim askTurn = new(1, 1);
    private readonly Dictionary<string, ToolKind> kinds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> commandArguments = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AllowRule> rules = [];
    private readonly List<AllowRule> sessionRules = [];
    private PermissionMode mode;

    /// <param name="ask">
    /// Asks the user about one call and returns the answer. A console's <c>ConfirmToolCallAsync</c> of <c>PinkRooster.SpectreConsole</c> fits as it is.
    /// </param>
    /// <exception cref="ArgumentNullException">The function is null.</exception>
    public PermissionPolicy(Func<ApprovalQuestion, CancellationToken, Task<ApprovalChoice>> ask)
    {
        this.ask = ask ?? throw new ArgumentNullException(nameof(ask));
    }

    /// <summary>The mode; <see cref="PermissionMode.Ask"/> until it is set. A change counts from the next call answered.</summary>
    public PermissionMode Mode
    {
        get
        {
            lock (gate)
            {
                return mode;
            }
        }
        set
        {
            lock (gate)
            {
                mode = value;
            }
        }
    }

    /// <summary>The rules given with <see cref="Allow"/>, such as those of a configuration file. <see cref="Reset"/> keeps them.</summary>
    public IReadOnlyList<AllowRule> Rules
    {
        get
        {
            lock (gate)
            {
                return [.. rules];
            }
        }
    }

    /// <summary>The rules the user added at a prompt with <see cref="ApprovalChoice.AllowForSession"/>. <see cref="Reset"/> drops them.</summary>
    public IReadOnlyList<AllowRule> SessionRules
    {
        get
        {
            lock (gate)
            {
                return [.. sessionRules];
            }
        }
    }

    /// <summary>Tells the policy the tools of these collections, so it knows the kind of each by name.</summary>
    /// <returns>This policy, for chaining.</returns>
    public PermissionPolicy WithTools(params ToolCollection[] collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        return WithTools(collections.SelectMany(collection => (collection ?? throw new ArgumentException("Every collection must be non-null.", nameof(collections))).GetAIFunctions()));
    }

    /// <summary>Tells the policy these tools, so it knows the kind of each by name.</summary>
    /// <remarks>Two tools of one name and different kinds leave that name without a kind, so the user is asked about it.</remarks>
    /// <returns>This policy, for chaining.</returns>
    public PermissionPolicy WithTools(IEnumerable<AITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        lock (gate)
        {
            foreach (AITool tool in tools)
            {
                ArgumentNullException.ThrowIfNull(tool, nameof(tools));
                ToolKind kind = tool.GetKind();
                kinds[tool.Name] = kinds.TryGetValue(tool.Name, out ToolKind known) && known != kind ? ToolKind.None : kind;
            }
        }
        return this;
    }

    /// <summary>
    /// Names the argument of a tool that is a command line, such as <c>("RunShell", "command")</c>. An
    /// <see cref="ApprovalChoice.AllowForSession"/> answer for that tool then keeps the start of the command instead of the whole
    /// tool: its first two words when the second is a subcommand (<c>dotnet build</c>), else its first word.
    /// </summary>
    /// <returns>This policy, for chaining.</returns>
    /// <exception cref="ArgumentException">A name is blank.</exception>
    public PermissionPolicy WithCommandArgument(string tool, string argument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);
        lock (gate)
        {
            commandArguments[tool.Trim()] = argument.Trim();
        }
        return this;
    }

    /// <summary>Adds standing rules, such as those of a configuration file. They count in every mode and outlive <see cref="Reset"/>.</summary>
    /// <returns>This policy, for chaining.</returns>
    public PermissionPolicy Allow(params AllowRule[] allowRules)
    {
        ArgumentNullException.ThrowIfNull(allowRules);
        if (allowRules.Any(rule => rule is null))
        {
            throw new ArgumentException("Every rule must be non-null.", nameof(allowRules));
        }
        lock (gate)
        {
            rules.AddRange(allowRules);
        }
        return this;
    }

    /// <summary>Goes back to <see cref="PermissionMode.Ask"/> and drops the rules the user added at a prompt, for a host that starts or resumes a conversation.</summary>
    public void Reset()
    {
        lock (gate)
        {
            mode = PermissionMode.Ask;
            sessionRules.Clear();
        }
    }

    /// <summary>Answers one tool call that needs approval: from the mode and the rules where they decide, else by asking the user.</summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">Cancels the wait for the user.</param>
    /// <returns>Whether the call runs; a refusal by the mode carries a reason that names the mode.</returns>
    public async Task<ApprovalAnswer> AnswerAsync(FunctionCallContent call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (Decide(call, out AllowRule? offer) is ApprovalAnswer decided)
        {
            return decided;
        }

        await askTurn.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // An answer given while this call waited its turn may cover it now, and the mode may have changed.
            if (Decide(call, out offer) is ApprovalAnswer covered)
            {
                return covered;
            }

            ApprovalChoice choice = await ask(new ApprovalQuestion(call, offer), cancellationToken).ConfigureAwait(false);
            if (choice == ApprovalChoice.AllowForSession && offer is not null)
            {
                lock (gate)
                {
                    sessionRules.Add(offer);
                }
            }
            return choice == ApprovalChoice.Skip ? ApprovalAnswer.Refuse() : ApprovalAnswer.Allow;
        }
        finally
        {
            askTurn.Release();
        }
    }

    // The answer the mode and the rules give, or null when the user must be asked; then the rule a session-wide answer would keep.
    private ApprovalAnswer? Decide(FunctionCallContent call, out AllowRule? offer)
    {
        offer = null;
        lock (gate)
        {
            ToolKind kind = kinds.GetValueOrDefault(call.Name, ToolKind.None);
            if (kind is ToolKind.Read or ToolKind.State)
            {
                return ApprovalAnswer.Allow;
            }

            if (mode == PermissionMode.Plan)
            {
                return kind != ToolKind.Edit && rules.Any(rule => rule.Matches(call))
                    ? ApprovalAnswer.Allow
                    : ApprovalAnswer.Refuse(
                        $"Refused: the permission mode is 'plan', and in plan mode {call.Name} does not run. Nothing was changed. " +
                        "Go on without it: read what you need and describe the change in your plan.");
            }

            if (rules.Any(rule => rule.Matches(call)) || sessionRules.Any(rule => rule.Matches(call))
                || (kind == ToolKind.Edit && mode == PermissionMode.AcceptEdits))
            {
                return ApprovalAnswer.Allow;
            }

            offer = commandArguments.TryGetValue(call.Name, out string? argument) ? AllowRule.ForCommandOf(call, argument) : AllowRule.ForTool(call.Name);
            return null;
        }
    }
}
