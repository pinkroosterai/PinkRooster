using Microsoft.Agents.AI;

namespace PinkRooster.Agents.Sessions;

/// <summary>A session read back by a <see cref="SessionStore"/>, and how the agent differs from the one that saved it.</summary>
/// <param name="Session">The session, ready for the agent's next run.</param>
/// <param name="Saved">The file it was read from.</param>
/// <param name="MissingTools">The tools the session was saved with that the agent no longer has. The history may still hold calls to them.</param>
/// <param name="NewTools">The tools the agent has now that it did not have when the session was saved.</param>
public sealed record RestoredSession(AgentSession Session, SavedSession Saved, IReadOnlyList<string> MissingTools, IReadOnlyList<string> NewTools)
{
    /// <summary>True when the agent's tool names are those the session was saved with.</summary>
    public bool SameTools => MissingTools.Count == 0 && NewTools.Count == 0;
}
