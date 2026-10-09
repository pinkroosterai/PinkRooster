using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PinkRooster.Agents.Eventing;

/// <summary>One agent's event handlers, and the logger its handler failures go to.</summary>
/// <remarks>A handler that throws is always caught and logged, to a null logger when there is none, and the run goes on. That includes an <see cref="OperationCanceledException"/>: a handler has no token, so its own timeout is not a cancelled run.</remarks>
internal sealed class EventPublisher(IReadOnlyList<Action<AgentEvent>> handlers, ILogger? logger, LiveEventHandlers? live = null)
{
    private readonly ILogger logger = logger ?? NullLogger.Instance;

    /// <summary>The handlers set when the agent was built, then those added to the instance since, in the order added.</summary>
    public IReadOnlyList<Action<AgentEvent>> Handlers
    {
        get
        {
            IReadOnlyList<Action<AgentEvent>> added = live?.Snapshot ?? [];
            return added.Count == 0 ? handlers : [.. handlers, .. added];
        }
    }

    public void Deliver(Action<AgentEvent> handler, AgentEvent item)
    {
        try
        {
            handler(item);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Agent event handler failed. RunId: {RunId}; EventType: {EventType}", item.RunId, item.GetType().Name);
        }
    }
}
