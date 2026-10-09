namespace PinkRooster.Agents.Eventing;

/// <summary>A step's reply was checked, for example by a reviewer step of a stepped agent.</summary>
/// <remarks>A stepped agent publishes it when it checks a reply; what a failed check does, such as sending the reply back with <paramref name="Feedback"/> or ending the run, is up to the agent.</remarks>
/// <param name="Label">The label of the checked step.</param>
/// <param name="Passed">Whether the reply passed.</param>
/// <param name="Feedback">What to fix, one problem per line, when it did not pass; otherwise null.</param>
public sealed record StepChecked(string Label, bool Passed, string? Feedback) : AgentEvent;
