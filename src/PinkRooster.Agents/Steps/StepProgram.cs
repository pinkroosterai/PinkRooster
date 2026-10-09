namespace PinkRooster.Agents.Steps;

/// <summary>A step loop: how a new message starts, and after each turn what comes next. <see cref="StepLoopAgent"/> runs one.</summary>
/// <remarks>
/// One instance serves every session, so a program keeps where a run stands in the session through <see cref="StepContext"/>, never in itself.
/// A <see cref="SteppedAgent"/> is one, through its two required members.
/// </remarks>
internal abstract class StepProgram
{
    /// <summary>The cap on turns per call; only a safety net under the program's own limits.</summary>
    public abstract int MaxTurns { get; }

    /// <summary>Starts a new message: sets the state and may enter the first step; the loop enters <c>start</c> when none was entered.</summary>
    public virtual ValueTask StartAsync(StepContext step, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Decides, after a turn, whether the run stops or which prompt goes next.</summary>
    public abstract ValueTask<NextStep> NextAsync(StepContext step, CancellationToken cancellationToken);
}
