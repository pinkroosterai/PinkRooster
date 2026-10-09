using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.SubAgents;

/// <summary>One model a sub-agent can run on.</summary>
/// <param name="Name">The name the calling model picks it by, such as <c>fast</c>.</param>
/// <param name="UseWhen">When to pick it, in a line the calling model reads, such as <c>Searching and summarising.</c></param>
/// <param name="ChatClient">The client that runs it.</param>
public sealed record SubAgentModel(string Name, string UseWhen, IChatClient ChatClient);
