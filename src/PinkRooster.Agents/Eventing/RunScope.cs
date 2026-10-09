using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.AI;

namespace PinkRooster.Agents.Eventing;

/// <summary>One run of a builder-made agent: its identity, the run that called it as a tool, and where its events go.</summary>
/// <remarks>
/// The current scope lives on the async flow, so the model-call client and the tool middleware below the run find it, and an agent
/// called as a tool inside the run finds its parent. A streaming run restores it after every yield, because an async iterator
/// resumes on its consumer's flow.
/// </remarks>
internal sealed class RunScope(string agentId, string? agentName, EventPublisher publisher, RunScope? parent)
{
    private readonly EventPublisher publisher = publisher;
    private readonly RunScope? parent = parent;
    // Read once, so a handler added to the instance during the run does not hear it.
    private readonly IReadOnlyList<Action<AgentEvent>> handlers = publisher.Handlers;
    private static readonly AsyncLocal<RunScope?> current = new();

    public static RunScope? Current
    {
        get => current.Value;
        set => current.Value = value;
    }

    public Guid RunId { get; } = Guid.NewGuid();

    private readonly long started = Stopwatch.GetTimestamp();
    private readonly object usageGate = new();
    private UsageDetails? usage;

    /// <summary>Time since the run began.</summary>
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(started);

    /// <summary>The sum of the usage of this run's own model calls so far, or null when none reported any.</summary>
    public UsageDetails? Usage
    {
        get
        {
            lock (usageGate)
            {
                if (usage is null)
                {
                    return null;
                }
                UsageDetails copy = new();
                copy.Add(usage);
                return copy;
            }
        }
    }

    /// <summary>A copy that later changes cannot reach: MAF's tool loop adds the usage of its later calls into the first response's own object.</summary>
    public static UsageDetails? Snapshot(UsageDetails? details)
    {
        if (details is null)
        {
            return null;
        }
        UsageDetails copy = new();
        copy.Add(details);
        return copy;
    }

    public void AddUsage(UsageDetails? added)
    {
        if (added is null)
        {
            return;
        }
        lock (usageGate)
        {
            (usage ??= new()).Add(added);
        }
    }

    /// <summary>Publishes to this agent's handlers, then to those of every agent whose run encloses it; each handler once.</summary>
    /// <param name="create">Makes the event; called only when some handler will receive it.</param>
    public void Publish(Func<AgentEvent> create)
    {
        AgentEvent? item = null;
        // Only a run inside another run can reach a handler twice; a top-level run delivers to its own list as it is.
        HashSet<Action<AgentEvent>>? delivered = parent is null ? null : [];
        for (RunScope? scope = this; scope is not null; scope = scope.parent)
        {
            foreach (Action<AgentEvent> handler in scope.handlers)
            {
                // A handler shared by nested agents, as with a cloned builder, hears each event once.
                if (delivered is not null && !delivered.Add(handler))
                {
                    continue;
                }
                item ??= create() with
                {
                    RunId = RunId,
                    ParentRunId = parent?.RunId,
                    AgentId = agentId,
                    AgentName = agentName,
                    Timestamp = DateTimeOffset.UtcNow
                };
                scope.publisher.Deliver(handler, item);
            }
        }
    }
}
