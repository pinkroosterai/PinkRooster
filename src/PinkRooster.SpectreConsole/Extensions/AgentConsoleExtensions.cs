using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents;
using PinkRooster.Agents.Permissions;
using Spectre.Console;

namespace PinkRooster.SpectreConsole;

/// <summary>Connects an agent to a Spectre.Console terminal in a fluent chain, and runs it there.</summary>
public static class AgentConsoleExtensions
{
    /// <summary>Draws this agent's runs on <paramref name="console"/>, and returns the agent so the call chains.</summary>
    /// <remarks>
    /// Creates one <see cref="AgentConsole"/> for the agent and subscribes its <see cref="AgentConsole.WriteEvent"/>; the subscription lasts as long as the agent.
    /// One console draws one run at a time, so do not share the agent between overlapping runs. For a console you keep, call <c>agent.OnEvent(console.WriteEvent)</c> yourself.
    /// </remarks>
    /// <typeparam name="TAgent">The agent class.</typeparam>
    /// <param name="agent">The agent to draw.</param>
    /// <param name="console">The console to draw on, such as <c>AnsiConsole.Console</c>.</param>
    /// <param name="options">Colours, limits and sensitive values; null uses the defaults.</param>
    /// <example>
    /// <code>
    /// await using ReviewerAgent reviewer = chatClient.CreateAgent&lt;ReviewerAgent&gt;().WithConsole(AnsiConsole.Console);
    /// </code>
    /// </example>
    public static TAgent WithConsole<TAgent>(this TAgent agent, IAnsiConsole console, AgentConsoleOptions? options = null) where TAgent : DeclaredAgent
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(console);
        _ = agent.OnEvent(new AgentConsole(console, options).WriteEvent);
        return agent;
    }

    /// <summary>Draws the runs of the agent this builder makes on <paramref name="console"/>, and returns the builder so the call chains.</summary>
    /// <remarks>
    /// Subscribes the console's <see cref="IAgentConsole.WriteEvent"/> with the builder's <c>OnEvent</c>. Keep the console: the same one answers
    /// approvals in <see cref="RunToConsoleAsync(AIAgent, string, IAgentConsole, AgentSession?, AgentRunOptions?, CancellationToken)"/> and asks the user's questions.
    /// One console draws one run at a time, so do not share it between overlapping runs.
    /// </remarks>
    /// <param name="builder">The builder of the agent to draw.</param>
    /// <param name="console">The console to draw on, such as <c>new AgentConsole(AnsiConsole.Console)</c>.</param>
    /// <example>
    /// <code>
    /// AgentConsole console = new(AnsiConsole.Console);
    /// AIAgent agent = chatClient.CreateAgent().WithRole("You are a build assistant.").WithConsole(console).Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithConsole(this AgentBuilder builder, IAgentConsole console)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(console);
        return builder.OnEvent(console.WriteEvent);
    }

    /// <summary>
    /// Runs the agent on a prompt, streaming, asks <paramref name="console"/> to confirm each tool call that needs approval, continues on the
    /// same session, and returns the whole response once no approval is left to ask.
    /// </summary>
    /// <remarks>
    /// The console given here only answers approvals, with allow or skip; what is drawn comes from the
    /// agent's events, so give the agent the same console with <c>WithConsole</c>. The response holds the updates of every run of the loop.
    /// </remarks>
    /// <param name="agent">The agent to run.</param>
    /// <param name="prompt">The user's message.</param>
    /// <param name="console">The console that asks whether a tool call may run.</param>
    /// <param name="session">The session to continue; null starts a new one.</param>
    /// <param name="options">Options for every run of the loop.</param>
    /// <param name="cancellationToken">Cancels the runs and the questions.</param>
    public static async Task<AgentResponse> RunToConsoleAsync(this AIAgent agent, string prompt, IAgentConsole console, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(console);
        return await agent.RunStreamingWithApprovalsAsync(prompt, session, console.ConfirmToolCallAsync, options, cancellationToken).ToAgentResponseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the agent on a prompt, streaming, has <paramref name="policy"/> answer each tool call that needs approval, continues on the
    /// same session, and returns the whole response once no approval is left to answer.
    /// </summary>
    /// <remarks>
    /// The policy asks the user through the function it was created with, so create it with the console's
    /// <see cref="IAgentConsole.ConfirmToolCallAsync"/>: <c>new PermissionPolicy(console.ConfirmToolCallAsync)</c>. An answer for the rest
    /// of the session is kept by the policy, so pass the same policy to every run. What is drawn comes from the agent's events, so
    /// give the agent the console with <c>WithConsole</c>.
    /// </remarks>
    /// <param name="agent">The agent to run.</param>
    /// <param name="prompt">The user's message.</param>
    /// <param name="policy">The policy that answers, and asks the user where its mode and rules do not decide.</param>
    /// <param name="session">The session to continue; null starts a new one.</param>
    /// <param name="options">Options for every run of the loop.</param>
    /// <param name="cancellationToken">Cancels the runs and the questions.</param>
    public static async Task<AgentResponse> RunToConsoleAsync(this AIAgent agent, string prompt, PermissionPolicy policy, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(policy);
        return await agent.RunStreamingWithApprovalsAsync(prompt, session, policy.AnswerAsync, options, cancellationToken).ToAgentResponseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the agent on a prompt as <see cref="RunToConsoleAsync(AIAgent, string, IAgentConsole, AgentSession?, AgentRunOptions?, CancellationToken)"/> does,
    /// and takes what the user types while it runs: a submitted line is posted to the agent's inbox and read by the model before its
    /// next model call, and Esc stops the run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Typing needs an <see cref="AgentConsole"/> on an interactive terminal that supports ANSI, and an agent with an inbox
    /// (<c>WithInbox</c> or <c>AllowBackground</c>). With any other console, or a terminal that is not interactive, the run is the same
    /// without input, and the agent needs no inbox.
    /// </para>
    /// <para>
    /// Every submitted line is delivered once. A line is drawn as queued and posted as soon as a run of the loop can read it; a line
    /// that came too late for the last model call is handed back in <see cref="ConsoleRunResult.UnreadLines"/>, to be the next prompt.
    /// A line <see cref="ConsoleRunOptions.HoldLine"/> names is never posted: it is drawn as held and handed back the same way. A typed
    /// line never interrupts a tool call.
    /// </para>
    /// <para>
    /// Esc cancels the run and returns with <see cref="ConsoleRunResult.Cancelled"/> set and the session as it stands; the caller's own
    /// token is not cancelled, and cancelling that one throws as usual. A line the inbox had taken and the model had not read when the
    /// run was stopped stays in the session, and its next run reads it. While an approval or a question is asked, the keyboard belongs to that prompt.
    /// </para>
    /// </remarks>
    /// <param name="agent">The agent to run.</param>
    /// <param name="prompt">The user's message.</param>
    /// <param name="console">The console that asks about approvals and, when it is an <see cref="AgentConsole"/>, reads the keyboard. Give the agent the same console with <c>WithConsole</c>.</param>
    /// <param name="session">The session to continue; null starts a new one, which the result returns.</param>
    /// <param name="options">A permission policy, the lines to hold back, and run options; null for none.</param>
    /// <param name="cancellationToken">Cancels the runs and the questions.</param>
    /// <exception cref="InvalidOperationException">The console can take input and the agent has no inbox; the message names the fix.</exception>
    public static async Task<ConsoleRunResult> RunWithInputAsync(this AIAgent agent, string prompt, IAgentConsole console, AgentSession? session = null, ConsoleRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(console);
        options ??= new ConsoleRunOptions();
        Func<FunctionCallContent, CancellationToken, Task<ApprovalAnswer>> answer = options.Policy is PermissionPolicy policy
            ? policy.AnswerAsync
            : async (call, token) => new ApprovalAnswer(await console.ConfirmToolCallAsync(call, token).ConfigureAwait(false));

        if (console is not AgentConsole { CanTakeInput: true } keys)
        {
            session ??= await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
            AgentResponse whole = await agent.RunStreamingWithApprovalsAsync(prompt, session, answer, options.RunOptions, cancellationToken).ToAgentResponseAsync(cancellationToken).ConfigureAwait(false);
            return new ConsoleRunResult(whole, session, Cancelled: false, UnreadLines: [], Draft: string.Empty);
        }
        if (!agent.HasInbox())
        {
            throw new InvalidOperationException(
                "A line typed during a run is posted to the agent's inbox, and this agent has none. Build it with AgentBuilder.WithInbox(), or with AllowBackground, " +
                $"which gives it one too; or run it with {nameof(RunToConsoleAsync)}, which takes no input.");
        }

        session ??= await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        AgentSession runSession = session;
        // Lines in the order typed. The reader calls submit and tick one after the other, so only the result below reads them from elsewhere, after the reader ended.
        List<(int Order, string Text)> toPost = [];
        List<(int Order, string Text)> held = [];
        int typed = 0;
        using CancellationTokenSource stopRun = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource stopKeys = new();

        string Submit(string line)
        {
            if (options.HoldLine?.Invoke(line) == true)
            {
                held.Add((typed++, line));
                return $"held until the run ends: {line}";
            }
            toPost.Add((typed++, line));
            return $"queued: {line}";
        }

        // The first line that waits goes to the inbox as soon as a run can read it: this run, a stepped agent's next step, or the run after an approval.
        async Task PostWaitingAsync()
        {
            while (toPost.Count > 0 && !stopRun.IsCancellationRequested
                   && await agent.TryPostMessageAsync(runSession, toPost[0].Text, CancellationToken.None).ConfigureAwait(false))
            {
                toPost.RemoveAt(0);
            }
        }

        Task<string> reading = keys.ReadInputAsync(Submit, stopRun.Cancel, PostWaitingAsync, stopKeys.Token);
        List<AgentResponseUpdate> updates = [];
        bool cancelled = false;
        string draft;
        try
        {
            await foreach (AgentResponseUpdate update in agent.RunStreamingWithApprovalsAsync(prompt, runSession, answer, options.RunOptions, stopRun.Token).ConfigureAwait(false))
            {
                updates.Add(update);
            }
        }
        catch (OperationCanceledException) when (stopRun.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }
        finally
        {
            await stopKeys.CancelAsync().ConfigureAwait(false);
            draft = await reading.ConfigureAwait(false);
        }

        return new ConsoleRunResult(
            updates.ToAgentResponse(),
            runSession,
            cancelled,
            [.. held.Concat(toPost).OrderBy(line => line.Order).Select(line => line.Text)],
            draft);
    }

    /// <summary>Asks whether a tool call may run, with allow and skip only. Returns false when the user skips it or nobody can answer.</summary>
    /// <remarks>It has the shape of the callback of <c>RunWithApprovalsAsync</c> and of a sub-agent collection's <c>ApproveToolCallsWith</c>.</remarks>
    /// <param name="console">The console that asks.</param>
    /// <param name="call">The call that waits for an answer.</param>
    /// <param name="cancellationToken">Cancels the question.</param>
    public static async Task<bool> ConfirmToolCallAsync(this IAgentConsole console, FunctionCallContent call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(call);
        return await console.ConfirmToolCallAsync(new ApprovalQuestion(call), cancellationToken).ConfigureAwait(false) != ApprovalChoice.Skip;
    }

    /// <summary>Runs the agent on a prompt, streaming, and returns the whole response once the run is complete.</summary>
    /// <remarks>
    /// What appears on the terminal comes from the agent's events, so the agent needs a console first: <see cref="WithConsole{TAgent}"/> or <c>OnEvent</c>. Without one the run is silent.
    /// A run that ends with an approval request returns there; the overload that takes an <see cref="IAgentConsole"/> asks and continues.
    /// </remarks>
    /// <param name="agent">The agent to run.</param>
    /// <param name="prompt">The user's message.</param>
    /// <param name="session">The session to continue; null starts a new one.</param>
    /// <param name="options">Options for this run.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public static async Task<AgentResponse> RunToConsoleAsync(this AIAgent agent, string prompt, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return await agent.RunStreamingAsync(prompt, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken).ConfigureAwait(false);
    }
}
