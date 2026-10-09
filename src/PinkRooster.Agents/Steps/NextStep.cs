using PinkRooster.Agents.Eventing;

namespace PinkRooster.Agents.Steps;

/// <summary>What a <see cref="SteppedAgent"/>'s <c>NextAsync</c> decides after a turn: stop, or send the next prompt.</summary>
public sealed class NextStep
{
    private static readonly NextStep StopStep = new(stops: true, label: null, prompt: null, isFinal: false);

    private NextStep(bool stops, string? label, string? prompt, bool isFinal)
    {
        Stops = stops;
        Label = label;
        Prompt = prompt;
        IsFinal = isFinal;
    }

    /// <summary>Ends the run; the last reply is the answer.</summary>
    public static NextStep Stop() => StopStep;

    /// <summary>Enters a new step and sends <paramref name="prompt"/> as a user message.</summary>
    /// <param name="label">The step's name, published on its <see cref="StepStarted"/> event.</param>
    /// <param name="prompt">What the step asks the model.</param>
    /// <param name="isFinal">Whether the loop stops after this step, without calling <c>NextAsync</c>. It does not say which reply is the answer.</param>
    /// <exception cref="ArgumentException">The label or the prompt is blank.</exception>
    public static NextStep Send(string label, string prompt, bool isFinal = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return new NextStep(stops: false, label, prompt, isFinal);
    }

    /// <summary>Sends <paramref name="prompt"/> as a user message in the step already in progress, such as a retry; no new step starts.</summary>
    /// <exception cref="ArgumentException">The prompt is blank.</exception>
    public static NextStep Send(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return new NextStep(stops: false, label: null, prompt, isFinal: false);
    }

    internal bool Stops { get; }

    internal string? Label { get; }

    internal string? Prompt { get; }

    internal bool IsFinal { get; }
}
