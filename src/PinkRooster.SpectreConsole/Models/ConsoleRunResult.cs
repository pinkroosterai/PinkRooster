using Microsoft.Agents.AI;

namespace PinkRooster.SpectreConsole;

/// <summary>How a run that took typed input ended: what the agent answered, and what the user typed that the model did not read.</summary>
/// <param name="Response">The updates of every run of the loop as one response; what had arrived when the run was cancelled, if it was.</param>
/// <param name="Session">The session the run was on, as it stands; the one that was created when none was given.</param>
/// <param name="Cancelled">True when the user stopped the run with Esc. The caller's own cancellation throws instead.</param>
/// <param name="UnreadLines">
/// The lines the user submitted that the model did not read, in the order typed: the lines the host named as not for the model, and
/// the lines that came too late for the run. The caller handles each as if it had been typed at its own prompt.
/// </param>
/// <param name="Draft">What was in the input line, not yet submitted, when the run ended; empty when nothing was.</param>
public sealed record ConsoleRunResult(AgentResponse Response, AgentSession Session, bool Cancelled, IReadOnlyList<string> UnreadLines, string Draft);
