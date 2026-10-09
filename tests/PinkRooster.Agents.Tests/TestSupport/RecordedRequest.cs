using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Tests.TestSupport;

/// <summary>One request as the fake client saw it.</summary>
internal sealed record RecordedRequest(List<string> Messages, ChatResponseFormat? Format, string? Instructions, List<string> ToolNames);
