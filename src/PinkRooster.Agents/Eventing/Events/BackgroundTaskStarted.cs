namespace PinkRooster.Agents.Eventing;

/// <summary>A tool call began as a background task. Pair it with its <see cref="BackgroundTaskEnded"/> by <paramref name="TaskId"/>.</summary>
/// <param name="TaskId">The task's id, counted from 1 within the session.</param>
/// <param name="ToolName">The tool that runs.</param>
/// <param name="CallId">The tool call that started the task, as on its <see cref="ToolCallStarted"/>; null when the tool loop did not say.</param>
public sealed record BackgroundTaskStarted(int TaskId, string ToolName, string? CallId) : AgentEvent;
