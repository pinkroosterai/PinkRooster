using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Steps;

/// <summary>MAF's loop evaluator for a <see cref="StepProgram"/>: after each turn it asks the program what comes next.</summary>
internal sealed class StepLoop(StepProgram program, ConditionalWeakTable<AgentSession, IReadOnlyList<ChatMessage>> inputs) : LoopEvaluator
{
    public override async ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default)
    {
        AgentSession session = context.Session
            ?? throw new InvalidOperationException("A step loop ran without a session; StepLoopAgent always supplies one.");
        inputs.TryGetValue(session, out IReadOnlyList<ChatMessage>? input);
        StepContext.RunPlace place = StepContext.CountTurn(session);
        // A final step ends the run, and so does the last turn MaxTurns allows; neither asks what comes next.
        if (place.IsFinal || place.Turn >= program.MaxTurns)
        {
            return LoopEvaluation.Stop();
        }
        StepContext step = new(session, input ?? [], context.LastResponse.Text);

        NextStep next = await program.NextAsync(step, cancellationToken).ConfigureAwait(false);
        if (next.Stops)
        {
            return LoopEvaluation.Stop();
        }
        if (next.Label is not null)
        {
            step.Enter(next.Label, next.IsFinal);
        }
        // Sent as a user message; the loop keeps it out of the caller's stream, not out of history.
        return LoopEvaluation.ContinueWithMessages([new ChatMessage(ChatRole.User, next.Prompt)]);
    }
}
