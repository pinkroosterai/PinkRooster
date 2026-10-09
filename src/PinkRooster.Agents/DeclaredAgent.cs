using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Briefs;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Shared;
using PinkRooster.Agents.Tools;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents;

/// <summary>
/// Base class for an agent defined as a class: the brief as attributes, its own tools as <see cref="ToolAttribute"/> methods, everything
/// else the builder offers in <see cref="Configure"/>. An instance is a MAF <see cref="AIAgent"/>.
/// </summary>
/// <remarks>
/// <para>
/// The agent is built through <see cref="AgentBuilder"/> once per instance, on first use and not in the constructor, so the class's own
/// constructor has run before its configuration is read. The order is fixed: the brief attributes, then the class's <see cref="ToolAttribute"/>
/// methods and <see cref="GetContextAsync"/>, then the collections named by <see cref="AgentToolsAttribute"/>, then <see cref="Configure"/>; the builder's rule for several sources decides a clash.
/// <c>GetService&lt;ChatClientAgent&gt;()</c> returns the inner agent, and builds it, so a mistake in the class surfaces there without a model call.
/// </para>
/// <para>
/// A class with a <see cref="ToolCollectionSourceAttribute"/>, such as an MCP server, connects during that build. A run, a new session and
/// <see cref="InitializeAsync"/> await the connection and pass their cancellation token to it. The members that cannot await
/// (<see cref="Name"/>, <see cref="Description"/>, <c>Id</c>, <see cref="GetService"/> and <see cref="AsAgentBuilder"/>) block the calling thread until
/// it is made, without a way to cancel, when they are the first to need the agent; call <see cref="InitializeAsync"/> first to avoid that.
/// </para>
/// <para>
/// One instance serves every session at once. What a run has to remember lives in the session, never in a field; a tool method or
/// <see cref="GetContextAsync"/> that needs the session of the run in progress reads <see cref="AIAgent.CurrentRunContext"/>. A run
/// started without a session gets one, so that is never null there.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [AgentRole("You review pull requests.")]
/// public sealed class ReviewerAgent(IChatClient chatClient) : DeclaredAgent(chatClient);
/// </code>
/// </example>
public abstract class DeclaredAgent : AIAgent, IAsyncDisposable
{
    private readonly IChatClient chatClient;
    private readonly SemaphoreSlim buildGate = new(1, 1);
    private readonly LiveEventHandlers liveHandlers = new();
    private readonly List<ToolCollection> sourced = [];
    private readonly List<Action<AgentBuilder>> builderCallbacks = [];
    private AIAgent? built;

    /// <param name="chatClient">The model client the agent uses.</param>
    /// <exception cref="ArgumentNullException">The client is null.</exception>
    protected DeclaredAgent(IChatClient chatClient)
    {
        this.chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    /// <summary>Assigns what the attributes cannot: tool collections that need constructor arguments, context providers, approvals, text known only at run time, and the rest of the builder.</summary>
    /// <remarks>
    /// Runs once per instance, when the agent is built, after the brief attributes, the class's own tools and the collections named by <see cref="AgentToolsAttribute"/> are on the builder. A derived class that
    /// overrides it without calling the base method drops what the base method assigned. Calling <c>base.Configure</c> is always allowed.
    /// </remarks>
    /// <param name="agent">The builder, already holding the attributes and the class's own tools.</param>
    protected virtual void Configure(AgentBuilder agent)
    {
    }

    /// <summary>Returns what the model should know about this agent's current state, or null for nothing; the rules are those of <see cref="ToolCollection.GetContextAsync"/>.</summary>
    /// <remarks>
    /// Asked before every model call, each call of the tool loop included, and sent in the one marked message that holds the collections' context,
    /// ahead of theirs. It is never stored in the session's history. Find the session of the run in progress through <see cref="AIAgent.CurrentRunContext"/>.
    /// </remarks>
    protected virtual ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

    /// <summary>Observes every event of this agent's runs, as a handler added with the builder's <c>OnEvent</c> would. It cannot change the run.</summary>
    /// <remarks>Called synchronously, possibly from several threads, with the events of agents this one calls as tools. A throw follows the builder's rule for a handler that throws.</remarks>
    /// <param name="item">The event.</param>
    protected virtual void OnEvent(AgentEvent item)
    {
    }

    /// <summary>Adds a handler for this agent's events, heard from the next run on. Dispose what it returns to remove the handler at once.</summary>
    /// <remarks>
    /// One instance serves every session, so the handler hears every session's runs and tells them apart by <see cref="AgentEvent.RunId"/>.
    /// Handlers are called after the class's own <see cref="OnEvent(AgentEvent)"/> and those <see cref="Configure"/> added. Adding from another thread is safe.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The handler is null.</exception>
    public IDisposable OnEvent(Action<AgentEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return liveHandlers.Add(handler);
    }

    /// <summary>Adds a handler for the events of one type, and the types derived from it; see <see cref="OnEvent(Action{AgentEvent})"/>.</summary>
    /// <typeparam name="TEvent">The event type to hear, such as <see cref="ToolCallCompleted"/>.</typeparam>
    public IDisposable OnEvent<TEvent>(Action<TEvent> handler) where TEvent : AgentEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnEvent(item =>
        {
            if (item is TEvent typed)
            {
                handler(typed);
            }
        });
    }

    /// <summary>
    /// Builds the agent now instead of on first use, so a mistake in the class or a server that cannot be reached surfaces at start-up.
    /// Does nothing when the agent is already built.
    /// </summary>
    /// <param name="cancellationToken">Cancels the build, and the connecting of the collections a <see cref="ToolCollectionSourceAttribute"/> creates.</param>
    /// <exception cref="InvalidOperationException">The class could not be assembled; the message names the class and the fix.</exception>
    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await GetInnerAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Returns a new builder holding everything this class declares, so a caller can add to it and build a separate agent.</summary>
    /// <remarks>
    /// <para>
    /// The builder holds the name, the brief attributes, the class's own tools and context, what a base class sets up and what <see cref="Configure"/> assigns, in the order
    /// <see cref="DeclaredAgent"/> describes. Each call runs <see cref="Configure"/> again and returns an independent builder. The agent it builds is not this instance:
    /// handlers added with <see cref="OnEvent(Action{AgentEvent})"/> do not hear its runs, the class's own <see cref="OnEvent(AgentEvent)"/> override does, and
    /// the class's own <see cref="ToolAttribute"/> methods still run on this instance, so they share its state.
    /// </para>
    /// <para>A class with a step loop already holds it, and a builder takes one: adding a second step loop throws, naming the fix.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The class could not be assembled; the message names the class and the fix.</exception>
    public AgentBuilder AsAgentBuilder() => CreateBuilder(listenLive: false);

    /// <summary>Adds a change to apply to the builder after <see cref="Configure"/>. For <c>CreateAgent&lt;TAgent&gt;(configure)</c>; must be called before the agent is first used.</summary>
    internal void AddBuilderCallback(Action<AgentBuilder> configure) => builderCallbacks.Add(configure);

    /// <summary>Sets up what a base class owns, such as a step loop, after the attributes and the own tools and before <see cref="Configure"/>, so a derived class cannot drop it.</summary>
    internal virtual void Prepare(AgentBuilder builder)
    {
    }

    internal bool OverridesContext =>
        GetType().GetMethod(nameof(GetContextAsync), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(CancellationToken)])?.DeclaringType != typeof(DeclaredAgent);

    internal ValueTask<string?> ReadContextAsync(CancellationToken cancellationToken) => GetContextAsync(cancellationToken);

    /// <inheritdoc />
    public override string? Name => Inner.Name;

    /// <inheritdoc />
    public override string? Description => Inner.Description;

    /// <inheritdoc />
    protected override string? IdCore => Inner.Id;

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : Inner.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    protected sealed override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        AIAgent inner = await GetInnerAsync(cancellationToken).ConfigureAwait(false);
        session ??= await inner.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        return await inner.RunAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected sealed override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AIAgent inner = await GetInnerAsync(cancellationToken).ConfigureAwait(false);
        session ??= await inner.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        await foreach (AgentResponseUpdate update in inner.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).CreateSessionAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    protected override async ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        await (await GetInnerAsync(cancellationToken).ConfigureAwait(false)).DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);

    /// <summary>The built agent, for the members that cannot await: when nothing has built it yet, the calling thread waits for the build.</summary>
    private AIAgent Inner
    {
        get
        {
            AIAgent? ready = Volatile.Read(ref built);
            if (ready is not null)
            {
                return ready;
            }
            buildGate.Wait();
            try
            {
                // A build that threw is not kept, so the next call builds again.
                return built ??= BuildBlocking();
            }
            finally
            {
                buildGate.Release();
            }
        }
    }

    /// <summary>The built agent, built on the first call without holding a thread while a sourced collection connects.</summary>
    private async ValueTask<AIAgent> GetInnerAsync(CancellationToken cancellationToken)
    {
        AIAgent? ready = Volatile.Read(ref built);
        if (ready is not null)
        {
            return ready;
        }
        await buildGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A build that threw or was cancelled is not kept, so the next call builds again.
            return built ??= await BuildAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            buildGate.Release();
        }
    }

    /// <summary>Disposes the collections created from <see cref="ToolCollectionSourceAttribute"/>s, such as an MCP connection.</summary>
    public async ValueTask DisposeAsync()
    {
        ToolCollection[] toDispose;
        lock (sourced)
        {
            toDispose = [.. sourced];
            sourced.Clear();
        }
        await AgentClassCollections.DisposeAsync(toDispose).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private AIAgent BuildBlocking()
    {
        try
        {
            return CreateBuilderBlocking(listenLive: true).Build();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw CouldNotBeBuilt(error);
        }
    }

    private async ValueTask<AIAgent> BuildAsync(CancellationToken cancellationToken)
    {
        try
        {
            AgentBuilder builder = Begin(listenLive: true);
            Complete(builder, await AgentClassCollections.CreateSourcedAsync(GetType(), cancellationToken).ConfigureAwait(false));
            return builder.Build();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw CouldNotBeBuilt(error);
        }
    }

    private AgentBuilder CreateBuilder(bool listenLive)
    {
        try
        {
            return CreateBuilderBlocking(listenLive);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw CouldNotBeBuilt(error);
        }
    }

    private AgentBuilder CreateBuilderBlocking(bool listenLive)
    {
        AgentBuilder builder = Begin(listenLive);
        Type type = GetType();
        // The caller cannot await, so this waits for the connecting; the thread pool has no context to deadlock on.
        Complete(builder, Task.Run(() => AgentClassCollections.CreateSourcedAsync(type, CancellationToken.None)).GetAwaiter().GetResult());
        return builder;
    }

    /// <summary>A builder holding what needs no connection: the name, the attributes, the class's own tools and the collections named by type.</summary>
    private AgentBuilder Begin(bool listenLive)
    {
        Type type = GetType();
        AgentBuilder builder = new AgentBuilder(chatClient)
            .WithName(type.Name)
            .UseMissingRoleMessage($"Add [AgentRole(\"...\")] to '{type.Name}', call WithRole in its Configure method, or set the whole prompt with WithSystemPrompt in Configure.")
            .OnEvent(item => OnEvent(item));
        if (listenLive)
        {
            builder.UseLiveHandlers(liveHandlers);
        }
        AgentClassSettings.Apply(type, builder);
        if (AgentClassBrief.Read(type) is AgentBrief brief)
        {
            builder.WithBrief(brief);
        }
        if (AgentOwnTools.Create(this) is ExternalToolCollection ownTools)
        {
            builder.WithTools(ownTools);
        }
        if (AgentClassCollections.Create(type) is { Count: > 0 } named)
        {
            builder.WithTools(named);
        }
        return builder;
    }

    /// <summary>Adds the sourced collections, which the agent now owns, then what a base class sets up, <see cref="Configure"/> and the callbacks.</summary>
    private void Complete(AgentBuilder builder, List<ToolCollection> created)
    {
        if (created.Count > 0)
        {
            lock (sourced)
            {
                sourced.AddRange(created);
            }
            builder.WithTools(created);
        }
        Prepare(builder);
        Configure(builder);
        foreach (Action<AgentBuilder> configure in builderCallbacks)
        {
            configure(builder);
        }
    }

    private InvalidOperationException CouldNotBeBuilt(Exception error) =>
        new($"Agent class '{GetType().Name}' could not be built: {error.Message}", error);
}
