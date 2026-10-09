namespace PinkRooster.Agents.Eventing;

/// <summary>Handlers added to an agent instance after it was built: heard from the next run on, and silent as soon as they are disposed.</summary>
internal sealed class LiveEventHandlers
{
    private readonly object gate = new();
    private Action<AgentEvent>[] handlers = [];

    /// <summary>The handlers now, in the order added. A run reads this once, when it starts, so a handler added later does not hear it.</summary>
    public IReadOnlyList<Action<AgentEvent>> Snapshot => Volatile.Read(ref handlers);

    public IDisposable Add(Action<AgentEvent> handler)
    {
        Registration registration = new(this, handler);
        lock (gate)
        {
            handlers = [.. handlers, registration.Handle];
        }
        return registration;
    }

    private void Remove(Action<AgentEvent> handle)
    {
        lock (gate)
        {
            handlers = [.. handlers.Where(existing => existing != handle)];
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly LiveEventHandlers owner;
        private readonly Action<AgentEvent> handler;
        private int disposed;

        public Registration(LiveEventHandlers owner, Action<AgentEvent> handler)
        {
            this.owner = owner;
            this.handler = handler;
            Handle = Invoke;
        }

        /// <summary>The delegate in the list. A run that already took its snapshot still holds it, so it checks the flag itself.</summary>
        public Action<AgentEvent> Handle { get; }

        private void Invoke(AgentEvent item)
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                handler(item);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Remove(Handle);
            }
        }
    }
}
