using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;

namespace PinkRooster.Agents.Steps;

/// <summary>What a <see cref="SteppedAgent"/>'s step loop is given: the session, the message's input, the last reply, the run's state and the step events.</summary>
/// <remarks>
/// One agent instance serves every session, so what a run has to remember goes in the session through <see cref="SetState{T}"/>, never in a field
/// of the class. Keep a state small and JSON-serializable: it travels with the session.
/// </remarks>
public sealed class StepContext
{
    private const string PlaceKey = "PinkRooster.Agents.Steps.Place";
    private const string StateKeyPrefix = "PinkRooster.Agents.Steps.State.";

    internal StepContext(AgentSession session, IReadOnlyList<ChatMessage> input, string lastReply)
    {
        Session = session;
        Input = input;
        LastReply = lastReply;
    }

    /// <summary>The session of the run. A run started without one gets one.</summary>
    public AgentSession Session { get; }

    /// <summary>The messages the caller sent in this call: the new message, or the tool-approval answers that continue a paused run.</summary>
    public IReadOnlyList<ChatMessage> Input { get; }

    /// <summary>The text of the user messages in <see cref="Input"/>: what the person asked in this message.</summary>
    public string Request => string.Join("\n", Input.Where(message => message.Role == ChatRole.User && message.Text.Length > 0).Select(message => message.Text));

    /// <summary>The turns finished for the current message: 0 in <c>StartAsync</c>, 1 in the first <c>NextAsync</c>. It continues across a tool approval.</summary>
    public int Turn => Place(Session).Turn;

    /// <summary>The reply of the turn that just ended. Empty in <c>StartAsync</c>, where no turn has run yet.</summary>
    public string LastReply { get; }

    /// <summary>Makes <paramref name="label"/> the step in progress and publishes its <see cref="StepStarted"/> event.</summary>
    /// <param name="label">The step's name.</param>
    /// <param name="isFinal">Whether the loop stops after this step, without asking <c>NextAsync</c> what follows. It does not say which reply is the answer.</param>
    public void Enter(string label, bool isFinal = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Session.StateBag.SetValue(PlaceKey, Place(Session) with { Label = label, IsFinal = isFinal });
        AgentEvents.Publish(new StepStarted(label, isFinal));
    }

    /// <summary>Publishes the outcome of checking a step's reply as a <see cref="StepChecked"/> event.</summary>
    /// <param name="label">The label of the checked step.</param>
    /// <param name="passed">Whether the reply passed.</param>
    /// <param name="feedback">What to fix when it did not pass; otherwise null.</param>
    public void Checked(string label, bool passed, string? feedback = null) =>
        AgentEvents.Publish(new StepChecked(label, passed, feedback));

    /// <summary>Keeps the run's state in the session, one state per type. Call it from <c>StartAsync</c>, and again to change it.</summary>
    public void SetState<T>(T state) where T : class
    {
        ArgumentNullException.ThrowIfNull(state);
        Session.StateBag.SetValue(StateKeyPrefix + typeof(T).FullName, state);
    }

    /// <summary>Reads the state <see cref="SetState{T}"/> kept.</summary>
    /// <exception cref="InvalidOperationException">The session holds none; <c>StartAsync</c> sets it before the first turn.</exception>
    public T GetState<T>() where T : class =>
        TryGetState<T>() ?? throw new InvalidOperationException(
            $"The session holds no {typeof(T).Name} state. Call SetState in StartAsync, which runs when a new message arrives, before NextAsync reads it.");

    /// <summary>Reads the state <see cref="SetState{T}"/> kept, or null when there is none.</summary>
    public T? TryGetState<T>() where T : class =>
        Session.StateBag.TryGetValue(StateKeyPrefix + typeof(T).FullName, out T? state) ? state : null;

    /// <summary>Forgets where the last message's run stood: a new message starts at turn 0 with no step.</summary>
    internal static void Clear(AgentSession session) => session.StateBag.TryRemoveValue(PlaceKey);

    /// <summary>Counts the turn that just ended and returns where the run now stands.</summary>
    internal static RunPlace CountTurn(AgentSession session)
    {
        RunPlace place = Place(session) with { Turn = Place(session).Turn + 1 };
        session.StateBag.SetValue(PlaceKey, place);
        return place;
    }

    internal static bool HasStep(AgentSession session) => Place(session).Label is not null;

    /// <summary>Continues the step a tool approval paused, publishing it again; false when the session has no step to continue.</summary>
    internal static bool Resume(AgentSession session)
    {
        RunPlace place = Place(session);
        if (place.Label is null)
        {
            return false;
        }
        AgentEvents.Publish(new StepStarted(place.Label, place.IsFinal));
        return true;
    }

    private static RunPlace Place(AgentSession session) =>
        session.StateBag.TryGetValue(PlaceKey, out RunPlace? place) && place is not null ? place : new RunPlace(null, false, 0);

    /// <summary>Where a message's run stands, kept in the session: the step in progress, whether it is final, and the turns finished.</summary>
    internal sealed record RunPlace(string? Label, bool IsFinal, int Turn);
}
