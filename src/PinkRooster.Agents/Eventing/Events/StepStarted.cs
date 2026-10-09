namespace PinkRooster.Agents.Eventing;

/// <summary>A step of a stepped agent's run began; plain agents publish none.</summary>
/// <remarks>
/// A stepped agent runs several model calls in one run; the reasoning, text and tool calls after this event belong to this step
/// until the next one. The answer is the reply of the last turn that ran, whichever step it belongs to: a step that is not final can
/// still hold it, when the agent ends the run early after a passed <see cref="StepChecked"/>.
/// </remarks>
/// <param name="Label">The step's name, such as <c>draft</c>, <c>execute</c> or <c>review</c>.</param>
/// <param name="IsFinal">Whether the loop stops after this step. It does not say which reply is the answer.</param>
public sealed record StepStarted(string Label, bool IsFinal) : AgentEvent;
