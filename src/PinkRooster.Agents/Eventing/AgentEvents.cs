
namespace PinkRooster.Agents.Eventing;

/// <summary>Publishes events of your own into the run in progress, from inside agent middleware added with <see cref="AgentBuilder.Use"/>.</summary>
public static class AgentEvents
{
    /// <summary>
    /// Publishes <paramref name="item"/> to the handlers of the run in progress on the current async flow, with that run's <see cref="AgentEvent.RunId"/>,
    /// agent and timestamp filled in.
    /// </summary>
    /// <param name="item">A <see cref="StepStarted"/>, a <see cref="StepChecked"/>, or an event type of your own that derives from <see cref="AgentEvent"/>.</param>
    /// <returns>True when a run was in progress; false, with nothing published, when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="item"/> is an event the agent publishes itself, about runs, model calls, tool calls, reasoning or answer text. Derive your own event from
    /// <see cref="AgentEvent"/> instead.
    /// </exception>
    public static bool Publish(AgentEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.GetType().Assembly == typeof(AgentEvent).Assembly && item is not (StepStarted or StepChecked))
        {
            throw new ArgumentException(
                $"{item.GetType().Name} is published by the agent itself, so publishing it here would break its once-per-run rules. " +
                $"Derive your own event from {nameof(AgentEvent)} and publish that, or use {nameof(StepStarted)} or {nameof(StepChecked)}.", nameof(item));
        }

        if (RunScope.Current is not RunScope scope)
        {
            return false;
        }
        scope.Publish(() => item);
        return true;
    }
}
