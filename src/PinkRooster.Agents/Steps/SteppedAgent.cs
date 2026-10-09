using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace PinkRooster.Agents.Steps;

/// <summary>Base class for a <see cref="DeclaredAgent"/> that takes several turns in one run: it says how a message starts and what follows each turn.</summary>
/// <remarks>
/// <para>
/// Every step stays in the session's history. <c>RunAsync</c> returns the last turn's reply; a
/// streamed run yields every turn, with the step prompts left out. A tool approval pauses the run, and the answers on the same session continue
/// it at the paused step. A layer <see cref="DeclaredAgent.Configure"/> adds wraps the whole loop.
/// </para>
/// <para>
/// One instance serves every session, so what a run has to remember lives in the session, in <see cref="StepContext.SetState{T}"/>, never in a field.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [AgentRole("You translate documents.")]
/// public sealed class TranslatorAgent(IChatClient chatClient) : SteppedAgent(chatClient)
/// {
///     protected override int MaxTurns => 3;
///
///     protected override ValueTask&lt;NextStep&gt; NextAsync(StepContext step, CancellationToken cancellationToken) =>
///         ValueTask.FromResult(step.LastReply.Contains("translated") ? NextStep.Stop() : NextStep.Send("revise", "Rewrite it in plain words."));
/// }
/// </code>
/// </example>
public abstract class SteppedAgent : DeclaredAgent
{
    /// <param name="chatClient">The model client the agent uses.</param>
    /// <exception cref="ArgumentNullException">The client is null.</exception>
    protected SteppedAgent(IChatClient chatClient)
        : base(chatClient)
    {
    }

    /// <summary>The most turns one message takes, whatever <see cref="NextAsync"/> says, a tool approval in between included; at least 1.</summary>
    protected abstract int MaxTurns { get; }

    /// <summary>Runs when a new message arrives: sets the run's state with <see cref="StepContext.SetState{T}"/> and may enter the first step with <see cref="StepContext.Enter"/>. By default it does nothing, and the loop enters a step labelled <c>start</c>.</summary>
    /// <param name="step">The session, the message's input and the step events; <see cref="StepContext.Turn"/> is 0.</param>
    /// <param name="cancellationToken">The run's cancellation token.</param>
    protected virtual ValueTask StartAsync(StepContext step, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Runs after each turn: returns <see cref="NextStep.Stop"/>, which makes the last reply the answer, or the next step. It is not called after a final step, nor after the turn that reaches <see cref="MaxTurns"/>.</summary>
    /// <param name="step">The session, the message's input, the last reply and the step events.</param>
    /// <param name="cancellationToken">The run's cancellation token.</param>
    protected abstract ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken);

    internal override void Prepare(AgentBuilder builder) =>
        builder.UseStepLoop(GetType().Name, CreateProgram);

    /// <summary>The steps this agent runs; at build, once per instance.</summary>
    /// <exception cref="InvalidOperationException"><see cref="MaxTurns"/> is below 1.</exception>
    internal virtual StepProgram CreateProgram(ILoggerFactory? loggerFactory)
    {
        int maxTurns = MaxTurns;
        if (maxTurns < 1)
        {
            throw new InvalidOperationException($"MaxTurns of '{GetType().Name}' is {maxTurns}; return at least 1, the most turns one run takes.");
        }
        return new Steps(this);
    }

    private sealed class Steps(SteppedAgent agent) : StepProgram
    {
        public override int MaxTurns => agent.MaxTurns;

        public override ValueTask StartAsync(StepContext step, CancellationToken cancellationToken) => agent.StartAsync(step, cancellationToken);

        public override ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken) => agent.NextAsync(step, cancellationToken);
    }
}
