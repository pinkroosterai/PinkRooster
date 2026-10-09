namespace PinkRooster.Agents.Briefs;

/// <summary>One example of what the agent is given and the exact reply it should make to it.</summary>
/// <param name="Input">What the agent is given.</param>
/// <param name="Output">What the agent must reply with.</param>
public sealed record AgentExample(string Input, string Output);
