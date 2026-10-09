using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Agents.Background;
using PinkRooster.Agents.Briefs;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Prompts;
using PinkRooster.Agents.Steps;
using PinkRooster.Agents.Tools;
using PinkRooster.ToolCollections.Context;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents;

/// <summary>
/// Fluent builder for a MAF agent: a system prompt composed from sections, tools and tool collections, and the MAF pieces an app
/// plugs in, then <see cref="Build"/>.
/// </summary>
/// <remarks>
/// <para>
/// Start one with <c>chatClient.CreateAgent()</c>. The builder is mutable: every method changes it and returns it. Role is the only
/// required section, unless a raw system prompt is set instead. The Instructions and Constraints sections list the builder's
/// <see cref="AgentDefaults"/> (the built-in set unless given others) first, then the text of each collection given to <see cref="WithTools(IEnumerable{ToolCollection})"/> in the order
/// added, then the builder's own; a line that repeats is sent once.
/// </para>
/// <para>
/// A built agent is a <see cref="ChatClientAgent"/> with MAF's default client wrapping, inside a thin wrapper that publishes
/// <see cref="OnEvent"/> events; <c>agent.GetService&lt;ChatClientAgent&gt;()</c> returns the inner agent. Collection context is added
/// inside the client, below MAF's tool loop, so every model call gets fresh context, the system prompt stays the same between calls,
/// and the context is never stored in the session's history.
/// </para>
/// </remarks>
public sealed class AgentBuilder
{
    private readonly IChatClient client;
    private readonly PromptSections sections;
    private readonly ToolSet toolSet;
    private readonly List<AIContextProvider> contextProviders;
    private readonly List<Action<ChatOptions>> chatOptionsCallbacks;
    private readonly List<Action<ChatClientAgentOptions>> agentOptionsCallbacks;
    private readonly List<Action<ChatClientBuilder>> clientCallbacks;
    private readonly List<Action<FunctionInvokingChatClient>> toolLoopCallbacks;
    private readonly List<Action<AgentEvent>> eventHandlers;
    private readonly List<Action<BackgroundOptions>> backgroundCallbacks;
    private readonly List<Layer> agentLayers;
    private string? id;
    private string? name;
    private string? description;
    private AgentDefaults? defaults;
    private ChatHistoryProvider? chatHistoryProvider;
    private ILoggerFactory? loggerFactory;
    private IServiceProvider? services;
    private LiveEventHandlers? liveHandlers;
    private bool inbox;

    /// <param name="chatClient">The model client every built agent uses. <c>chatClient.CreateAgent()</c> is the shorter way in.</param>
    public AgentBuilder(IChatClient chatClient)
    {
        client = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        sections = new PromptSections();
        toolSet = new ToolSet();
        contextProviders = [];
        chatOptionsCallbacks = [];
        agentOptionsCallbacks = [];
        clientCallbacks = [];
        toolLoopCallbacks = [];
        eventHandlers = [];
        backgroundCallbacks = [];
        agentLayers = [];
    }

    private AgentBuilder(AgentBuilder source)
    {
        client = source.client;
        sections = source.sections.Clone();
        toolSet = source.toolSet.Clone();
        contextProviders = [.. source.contextProviders];
        chatOptionsCallbacks = [.. source.chatOptionsCallbacks];
        agentOptionsCallbacks = [.. source.agentOptionsCallbacks];
        clientCallbacks = [.. source.clientCallbacks];
        toolLoopCallbacks = [.. source.toolLoopCallbacks];
        eventHandlers = [.. source.eventHandlers];
        backgroundCallbacks = [.. source.backgroundCallbacks];
        agentLayers = [.. source.agentLayers];
        id = source.id;
        name = source.name;
        description = source.description;
        defaults = source.defaults;
        chatHistoryProvider = source.chatHistoryProvider;
        loggerFactory = source.loggerFactory;
        services = source.services;
        liveHandlers = source.liveHandlers;
        inbox = source.inbox;
    }

    /// <summary>Sets who the agent is. Required unless <see cref="WithSystemPrompt"/> is used.</summary>
    public AgentBuilder WithRole(string role)
    {
        sections.Role = Require(role, nameof(role));
        return this;
    }

    /// <summary>Sets what success looks like for the agent.</summary>
    public AgentBuilder WithObjective(string objective)
    {
        sections.Objective = Require(objective, nameof(objective));
        return this;
    }

    /// <summary>Sets background the agent should know.</summary>
    public AgentBuilder WithBackground(string background)
    {
        sections.Background = Require(background, nameof(background));
        return this;
    }

    /// <summary>Adds an instruction.</summary>
    public AgentBuilder WithInstruction(string instruction) => Add(sections.Instructions, instruction, nameof(instruction));

    /// <summary>Adds instructions, after any already added.</summary>
    public AgentBuilder WithInstructions(IEnumerable<string> instructions) => AddAll(sections.Instructions, instructions, nameof(instructions));

    /// <summary>Adds a boundary the agent must not cross.</summary>
    public AgentBuilder WithConstraint(string constraint) => Add(sections.Constraints, constraint, nameof(constraint));

    /// <summary>Adds constraints, after any already added.</summary>
    public AgentBuilder WithConstraints(IEnumerable<string> constraints) => AddAll(sections.Constraints, constraints, nameof(constraints));

    /// <summary>Sets the shape the agent's response must take.</summary>
    public AgentBuilder WithOutputFormat(string outputFormat)
    {
        sections.OutputFormat = Require(outputFormat, nameof(outputFormat));
        return this;
    }

    /// <summary>Adds an input and output example.</summary>
    public AgentBuilder WithExample(string input, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        sections.Examples.Add(new AgentExample(input, output));
        return this;
    }

    /// <summary>Uses these defaults instead of <see cref="AgentDefaults.BuiltIn"/>. Tool collections' text still goes in.</summary>
    public AgentBuilder WithDefaults(AgentDefaults defaults)
    {
        this.defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        return this;
    }

    /// <summary>Leaves the defaults out of this agent's prompt: the same as <c>WithDefaults(AgentDefaults.None)</c>.</summary>
    public AgentBuilder WithoutDefaults() => WithDefaults(AgentDefaults.None);

    /// <summary>Adds the sections of a brief, as if the section methods had been called in the same order.</summary>
    /// <remarks>
    /// A single-value section the brief sets (role, objective, background, output format, system prompt) replaces what an earlier call set, and a later call
    /// replaces it in turn. Instructions, constraints and examples are added after those already there.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The brief is null.</exception>
    public AgentBuilder WithBrief(AgentBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        sections.Apply(brief);
        return this;
    }

    /// <summary>Sets the whole system prompt as given, instead of the sections. <see cref="Build"/> throws if a section is set too.</summary>
    public AgentBuilder WithSystemPrompt(string systemPrompt)
    {
        sections.SystemPrompt = Require(systemPrompt, nameof(systemPrompt));
        return this;
    }

    /// <summary>Sets the agent's ID. Without it the agent generates one.</summary>
    public AgentBuilder WithId(string id) => Set(ref this.id, id, nameof(id));

    /// <summary>Sets the agent's name.</summary>
    public AgentBuilder WithName(string name) => Set(ref this.name, name, nameof(name));

    /// <summary>Sets the agent's description.</summary>
    public AgentBuilder WithDescription(string description) => Set(ref this.description, description, nameof(description));

    /// <summary>Adds a tool.</summary>
    public AgentBuilder WithTool(AITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        toolSet.AddTool(tool);
        return this;
    }

    /// <summary>Adds a method marked with <see cref="ToolAttribute"/> as a tool, named, described and approval-marked the way a <see cref="ToolCollection"/> does it.</summary>
    /// <param name="method">The method, such as <c>clock.GetTime</c>; an instance method keeps its target.</param>
    /// <exception cref="ArgumentException">The method has no <see cref="ToolAttribute"/>.</exception>
    public AgentBuilder WithTool(Delegate method)
    {
        ArgumentNullException.ThrowIfNull(method);
        ToolAttribute toolAttribute = method.Method.GetCustomAttribute<ToolAttribute>()
            ?? throw new ArgumentException($"Method '{method.Method.Name}' has no [Tool] attribute; mark it with [Tool], or call WithTool(method, name, description).", nameof(method));
        return WithTool(ToolCollection.CreateTool(method));
    }

    /// <summary>Adds a method or lambda as a tool with the given name and description.</summary>
    /// <param name="method">The method or lambda to call.</param>
    /// <param name="name">The tool name the model sees.</param>
    /// <param name="description">Optional. Without it the method's <see cref="System.ComponentModel.DescriptionAttribute"/> is used, if any.</param>
    public AgentBuilder WithTool(Delegate method, string name, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return WithTool(AIFunctionFactory.Create(method, name, description));
    }

    /// <summary>Adds tools, after any already added.</summary>
    public AgentBuilder WithTools(IEnumerable<AITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        AITool[] copy = [.. tools];
        if (copy.Any(tool => tool is null))
        {
            throw new ArgumentException("Every tool must be non-null.", nameof(tools));
        }
        toolSet.AddTools(copy);
        return this;
    }

    /// <summary>
    /// Adds every tool of each collection, adds their <see cref="ToolCollection.Instructions"/> and <see cref="ToolCollection.Constraints"/>
    /// to the system prompt, and sends their <see cref="ToolCollection.GetContextAsync"/> text on every model call.
    /// </summary>
    /// <remarks>A collection's state is shared by every agent and session it is given to.</remarks>
    /// <param name="collections">One collection, several, or a list of them.</param>
    public AgentBuilder WithTools(params IEnumerable<ToolCollection> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        ToolCollection[] copy = [.. collections];
        if (copy.Any(collection => collection is null))
        {
            throw new ArgumentException("Every tool collection must be non-null.", nameof(collections));
        }
        toolSet.AddCollections(copy);
        return this;
    }

    /// <summary>
    /// Makes the named tools need the host's approval before they run: a run that calls one ends with a
    /// <see cref="ToolApprovalRequestContent"/>, and the tool runs once the approval is sent back on the same session.
    /// </summary>
    /// <remarks>Names are matched ignoring case, at <see cref="Build"/>, against every tool the builder has, collections included.</remarks>
    public AgentBuilder RequireApproval(params string[] toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        foreach (string toolName in toolNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolName, nameof(toolNames));
        }
        toolSet.RequireApproval(toolNames);
        return this;
    }

    /// <summary>
    /// Lets the model start the named tools in the background: each gets one more optional parameter, <c>runInBackground</c>. A call
    /// with it returns a task id at once, and the model follows the task with the tools <c>WaitForTasks</c>, <c>GetTaskResult</c>, <c>ListTasks</c> and <c>CancelTask</c>,
    /// which the agent then has too. A call without it runs as before.
    /// </summary>
    /// <remarks>
    /// Naming a tool says it may run at the same time as other calls on the same collection instance; the builder cannot check that.
    /// Names are matched ignoring case, at <see cref="Build"/>, against every tool the builder has, collections included. A task belongs
    /// to the run that started it: when the run ends its running tasks are cancelled, except across a pause for an approval.
    /// The agent also gets an inbox, as with <see cref="WithInbox"/>.
    /// </remarks>
    public AgentBuilder AllowBackground(params string[] toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        foreach (string toolName in toolNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolName, nameof(toolNames));
        }
        toolSet.AllowBackground(toolNames);
        return this;
    }

    /// <summary>
    /// Gives the agent an inbox: while a run is in progress, <c>agent.PostMessageAsync(session, message)</c> hands it a message, which the
    /// model reads before its next model call. <see cref="AllowBackground"/> gives one too.
    /// </summary>
    /// <remarks>
    /// The inbox is MAF's message injection, with chat history saved after every model call instead of once at the end of the run, so a
    /// posted message stays in the session's history. With <see cref="ConfigureToolLoop"/> a message is read one model call later.
    /// </remarks>
    public AgentBuilder WithInbox()
    {
        inbox = true;
        return this;
    }

    /// <summary>Adds a callback that changes the limits on background tasks, such as <see cref="BackgroundOptions.MaxRunningTasks"/>.</summary>
    /// <remarks>Callbacks run in the order they were added, on fresh options per build. They need <see cref="AllowBackground"/> or <see cref="WithInbox"/>.</remarks>
    public AgentBuilder ConfigureBackground(Action<BackgroundOptions> configure) => AddCallback(backgroundCallbacks, configure);

    /// <summary>Sets where the agent keeps each session's chat history. Without it MAF keeps it in memory, in the session.</summary>
    public AgentBuilder WithChatHistoryProvider(ChatHistoryProvider chatHistoryProvider)
    {
        this.chatHistoryProvider = chatHistoryProvider ?? throw new ArgumentNullException(nameof(chatHistoryProvider));
        return this;
    }

    /// <summary>Adds a MAF context provider, such as compaction, memory or retrieval. Providers run in the order added.</summary>
    public AgentBuilder WithContextProvider(AIContextProvider contextProvider)
    {
        ArgumentNullException.ThrowIfNull(contextProvider);
        contextProviders.Add(contextProvider);
        return this;
    }

    /// <summary>Adds a callback that changes the chat options after instructions and tools are set, for settings the builder does not cover.</summary>
    /// <remarks>Callbacks run in the order they were added, on fresh options per build, and may overwrite what the builder set.</remarks>
    public AgentBuilder ConfigureChatOptions(Action<ChatOptions> configure) => AddCallback(chatOptionsCallbacks, configure);

    /// <summary>Adds a callback that changes the agent options last, for MAF settings the builder does not cover.</summary>
    /// <remarks>Callbacks run in the order they were added, on fresh options per build, and may overwrite what the builder set.</remarks>
    public AgentBuilder ConfigureAgentOptions(Action<ChatClientAgentOptions> configure) => AddCallback(agentOptionsCallbacks, configure);

    /// <summary>Adds client middleware, such as logging, OpenTelemetry or caching, between the collections' context and the model client.</summary>
    /// <remarks>Callbacks run in the order they were added; middleware added first sits nearest the context.</remarks>
    public AgentBuilder ConfigureClient(Action<ChatClientBuilder> configure) => AddCallback(clientCallbacks, configure);

    /// <summary>
    /// Adds a callback that changes the agent's tool loop, such as <see cref="FunctionInvokingChatClient.MaximumIterationsPerRequest"/>
    /// or <see cref="FunctionInvokingChatClient.IncludeDetailedErrors"/>.
    /// </summary>
    /// <remarks>
    /// The builder then supplies the tool loop itself and MAF uses it instead of its own; MAF's other defaults stay. Callbacks run in
    /// the order added, on a new loop per build.
    /// </remarks>
    public AgentBuilder ConfigureToolLoop(Action<FunctionInvokingChatClient> configure) => AddCallback(toolLoopCallbacks, configure);

    /// <summary>
    /// Adds a handler for the agent's events — each run's start and end, reasoning and answer text as it arrives, and every tool
    /// call's request, approval, start and end — for streaming and non-streaming runs alike.
    /// </summary>
    /// <remarks>
    /// Handlers are called synchronously, in the order added, on the thread that produced the event; parallel tool calls can call them
    /// from several threads at once. They also receive the events of any builder-made agent this agent calls as a tool, marked by
    /// <see cref="AgentEvent.ParentRunId"/>; a handler registered on both hears each event once. A handler that throws is always caught and logged
    /// (to nowhere when neither <see cref="WithLoggerFactory"/> nor <see cref="WithServices"/> gives a logger factory), and the run goes on; that includes an <see cref="OperationCanceledException"/>.
    /// </remarks>
    public AgentBuilder OnEvent(Action<AgentEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        eventHandlers.Add(handler);
        return this;
    }

    /// <summary>Adds a handler for the events of one type, and the types derived from it, in the same list as <see cref="OnEvent(Action{AgentEvent})"/>.</summary>
    /// <remarks>Order, cloning and the rule that a handler shared by nested agents hears each event once are those of <see cref="OnEvent(Action{AgentEvent})"/>. <c>OnEvent&lt;AgentEvent&gt;</c> hears everything.</remarks>
    /// <typeparam name="TEvent">The event type to hear, such as <see cref="ToolCallCompleted"/>.</typeparam>
    public AgentBuilder OnEvent<TEvent>(Action<TEvent> handler) where TEvent : AgentEvent
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
    /// Sets the logger factory the agent uses, and where a failing handler or collection context is logged. Without it the agent takes the
    /// <see cref="ILoggerFactory"/> of the provider given to <see cref="WithServices"/>; with neither, those failures are not logged anywhere.
    /// </summary>
    public AgentBuilder WithLoggerFactory(ILoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        return this;
    }

    /// <summary>Sets the service provider that the <see cref="Use"/> callbacks, the client pipeline and the <see cref="ChatClientAgent"/> receive. Without it callbacks get an empty one. An <see cref="ILoggerFactory"/> registered in it is used when <see cref="WithLoggerFactory"/> was not called.</summary>
    public AgentBuilder WithServices(IServiceProvider services)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        return this;
    }

    /// <summary>Runs <paramref name="configure"/> on this builder, so one method can apply shared settings to many builders.</summary>
    public AgentBuilder Apply(Action<AgentBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(this);
        return this;
    }

    /// <summary>Returns a new builder with a copy of every setting; changing either leaves the other as it was.</summary>
    /// <remarks>The client, tools, collections, providers and callbacks themselves are shared, not copied.</remarks>
    public AgentBuilder Clone() => new(this);

    /// <summary>Builds a new agent from the builder's current settings. Can be called more than once; each agent is independent.</summary>
    /// <exception cref="InvalidOperationException">
    /// Role and raw system prompt are both missing, both a raw system prompt and a section are set, two tools share a name,
    /// <see cref="RequireApproval"/> or <see cref="AllowBackground"/> names a tool the builder does not have, <see cref="ConfigureBackground"/> was used
    /// without <see cref="AllowBackground"/> or <see cref="WithInbox"/> or set a limit that cannot work, a <see cref="ConfigureAgentOptions"/> callback switched off what the inbox needs, a collection attribute holds blank text, a <see cref="Use"/> callback returned null,
    /// or two step loops were added (each stepped agent adds one).
    /// </exception>
    public AIAgent Build()
    {
        ILoggerFactory? loggers = Loggers;
        string[] loops = [.. agentLayers.Where(layer => layer.StepLoopOwner is not null).Select(layer => layer.StepLoopOwner!)];
        if (loops.Length > 1)
        {
            throw new InvalidOperationException(
                $"This builder has {loops.Length} step loops ({string.Join(", ", loops)}), and one agent runs one. Keep one of them: " +
                "a SteppedAgent's own steps, and remove the others.");
        }

        BackgroundOptions? background = CreateBackgroundOptions();
        ToolSet tools = ToolsFor(background);
        ChatClientAgentOptions options = CreateOptions(tools);
        if (background is not null)
        {
            // The inbox is MAF's message injection; saving history per model call is what keeps an injected message in the session's history.
            options.EnableMessageInjection = true;
            options.RequirePerServiceCallChatHistoryPersistence = true;
        }

        ChatClientBuilder pipeline = client.AsBuilder();
        if (background is not null)
        {
            // History saved per model call can hold a tool call whose run was cancelled before its result; see the client.
            pipeline.Use(inner => new UnansweredToolCallChatClient(inner));
        }
        if (toolLoopCallbacks.Count > 0)
        {
            pipeline.UseFunctionInvocation(loggers, loop =>
            {
                foreach (Action<FunctionInvokingChatClient> configure in toolLoopCallbacks)
                {
                    configure(loop);
                }
            });
        }
        if (tools.Collections.Count > 0)
        {
            ToolCollection[] sources = [.. tools.Collections];
            ILogger? logger = loggers?.CreateLogger<ToolCollectionChatClient>();
            pipeline.Use(inner => new ToolCollectionChatClient(inner, sources, logger));
        }
        // Above ConfigureClient middleware, so what a caller's decorator adds to a response, such as reasoning, is published too.
        pipeline.Use(inner => new EventingChatClient(inner));
        foreach (Action<ChatClientBuilder> configure in clientCallbacks)
        {
            configure(pipeline);
        }

        foreach (Action<ChatClientAgentOptions> configure in agentOptionsCallbacks)
        {
            configure(options);
        }
        if (background is not null)
        {
            if (!options.EnableMessageInjection || !options.RequirePerServiceCallChatHistoryPersistence)
            {
                throw new InvalidOperationException(
                    $"The inbox needs {nameof(ChatClientAgentOptions.EnableMessageInjection)} and {nameof(ChatClientAgentOptions.RequirePerServiceCallChatHistoryPersistence)}, " +
                    $"and a {nameof(ConfigureAgentOptions)} callback switched one off. Leave both on, or remove {nameof(AllowBackground)} and {nameof(WithInbox)}.");
            }
            if (toolLoopCallbacks.Count > 0)
            {
                loggers?.CreateLogger<AgentBuilder>().LogWarning(
                    "This agent has an inbox and a tool loop from ConfigureToolLoop. MAF places its message injection above a tool loop the builder supplies, " +
                    "so a posted message or task update is read one model call later, when that loop's turn has ended.");
            }
        }
        ChatClientAgent agent = new(pipeline.Build(services), options, loggers, services);

        // Outside in: events, the layers last added first (a step loop among them), background tasks, tool-call events, the agent. One caller run is one event run, whatever the steps.
        AIAgent built = agent.AsBuilder().Use(ToolCallEvents.InvokeAsync).Build();
        if (background is not null)
        {
            built = new BackgroundRunAgent(built, background, loggers);
        }
        foreach (Layer layer in agentLayers)
        {
            built = layer.Wrap(built, services, loggers);
        }

        // Wired even without handlers, so an agent called as another's tool reports to its caller's handlers.
        EventPublisher publisher = new([.. eventHandlers], loggers?.CreateLogger<AgentBuilder>(), liveHandlers);
        return new EventingAgent(built, publisher);
    }

    /// <summary>Has the agent hear the handlers added to <paramref name="handlers"/> later too, from each run that starts after they were added. For <see cref="DeclaredAgent"/>.</summary>
    internal AgentBuilder UseLiveHandlers(LiveEventHandlers handlers)
    {
        liveHandlers = handlers;
        return this;
    }

    /// <summary>Replaces the message of the error for a missing role, so a <see cref="DeclaredAgent"/> class can name its own fix.</summary>
    internal AgentBuilder UseMissingRoleMessage(string message)
    {
        sections.MissingRoleMessage = message;
        return this;
    }

    /// <summary>Adds agent middleware, MAF's shape: a callback that wraps the agent so far, inside the event layer, such as a loop, a reviewer or a router.</summary>
    /// <remarks>
    /// Callbacks are applied in the order added, each around the one before, so the first sits nearest the agent.
    /// The callback runs inside the run the event layer opens, so <see cref="AgentEvents.Publish"/> reaches this agent's handlers.
    /// <see cref="BuildOptions"/> refuses a builder with middleware, as it builds no agent.
    /// </remarks>
    /// <param name="agentFactory">Receives the agent so far and the provider given to <see cref="WithServices"/> (an empty one when none was), and returns the wrapping agent.</param>
    /// <exception cref="InvalidOperationException">At <see cref="Build"/>: the callback returned null.</exception>
    public AgentBuilder Use(Func<AIAgent, IServiceProvider, AIAgent> agentFactory)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        agentLayers.Add(new Layer((agent, services, _) => new AIAgentBuilder(agent)
            .Use((inner, provider) => agentFactory(inner, provider)
                ?? throw new InvalidOperationException($"A callback given to {nameof(Use)} returned null; return the agent it was given, or an agent that wraps it."))
            .Build(services), StepLoopOwner: null));
        return this;
    }

    /// <summary>Adds the builder's one step loop in the place of this call among the <see cref="Use"/> callbacks; a second one fails <see cref="Build"/>.</summary>
    /// <param name="owner">What added it, for the error message: the stepped agent's class.</param>
    /// <param name="createProgram">Makes the program the loop runs, given the builder's logger factory; called at each build.</param>
    internal AgentBuilder UseStepLoop(string owner, Func<ILoggerFactory?, StepProgram> createProgram)
    {
        agentLayers.Add(new Layer((agent, _, loggers) => new StepLoopAgent(agent, createProgram(loggers), loggers), owner));
        return this;
    }

    /// <summary>
    /// Returns the agent options alone, for building the agent yourself, such as <c>new ChatClientAgent(client, options)</c>.
    /// Collection context then comes once per run, through a context provider, instead of once per model call.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// As <see cref="Build"/>, or <see cref="ConfigureClient"/> or <see cref="ConfigureToolLoop"/> was used: they shape the client,
    /// which this method does not build; or <see cref="OnEvent"/> was used: events come from the agent, which it does not build either.
    /// </exception>
    public ChatClientAgentOptions BuildOptions()
    {
        ILoggerFactory? loggers = Loggers;
        if (agentLayers.Count > 0)
        {
            throw new InvalidOperationException(
                $"A callback added with {nameof(Use)} runs in the agent {nameof(Build)}() makes, and {nameof(BuildOptions)}() makes none. " +
                $"Call {nameof(Build)}() instead, or start from CreateAgent().");
        }
        if (eventHandlers.Count > 0)
        {
            throw new InvalidOperationException(
                $"{nameof(OnEvent)} handlers are called by the agent {nameof(Build)}() makes, and {nameof(BuildOptions)}() makes none. " +
                $"Call {nameof(Build)}() instead, or remove the {nameof(OnEvent)} handlers.");
        }
        if (toolSet.HasBackground || inbox || backgroundCallbacks.Count > 0)
        {
            throw new InvalidOperationException(
                $"Background tasks and the inbox are kept by the agent {nameof(Build)}() makes, and {nameof(BuildOptions)}() makes none. " +
                $"Call {nameof(Build)}() instead, or remove {nameof(AllowBackground)}, {nameof(WithInbox)} and {nameof(ConfigureBackground)}.");
        }
        if (clientCallbacks.Count > 0 || toolLoopCallbacks.Count > 0)
        {
            throw new InvalidOperationException(
                $"{nameof(ConfigureClient)} and {nameof(ConfigureToolLoop)} shape the chat client, which {nameof(BuildOptions)}() does not build. " +
                $"Apply them to your own client instead, or call {nameof(Build)}().");
        }

        ChatClientAgentOptions options = CreateOptions(toolSet);
        if (toolSet.Collections.Count > 0)
        {
            options.AIContextProviders =
            [
                .. options.AIContextProviders ?? [],
                ToolCollectionContextProvider.ForContextOnly(toolSet.Collections, loggers?.CreateLogger<ToolCollectionContextProvider>())
            ];
        }
        foreach (Action<ChatClientAgentOptions> configure in agentOptionsCallbacks)
        {
            configure(options);
        }
        return options;
    }

    // The factory given by name, else the one a host registered in the services it passed.
    private ILoggerFactory? Loggers => loggerFactory ?? services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;

    private sealed record Layer(Func<AIAgent, IServiceProvider?, ILoggerFactory?, AIAgent> Wrap, string? StepLoopOwner);

    /// <summary>The limits for this build, or null when the agent has neither a background tool nor an inbox.</summary>
    private BackgroundOptions? CreateBackgroundOptions()
    {
        if (!toolSet.HasBackground && !inbox)
        {
            if (backgroundCallbacks.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(ConfigureBackground)} sets the limits on background tasks, but no tool is allowed in the background. " +
                    $"Name the tools with {nameof(AllowBackground)}, or remove {nameof(ConfigureBackground)}.");
            }
            return null;
        }

        BackgroundOptions options = new();
        foreach (Action<BackgroundOptions> configure in backgroundCallbacks)
        {
            configure(options);
        }
        options.Validate();
        return options;
    }

    // The builder's own tool set stays as the caller made it; the task tools join a copy, once per build.
    private ToolSet ToolsFor(BackgroundOptions? background)
    {
        if (background is null || !toolSet.HasBackground)
        {
            return toolSet;
        }
        ToolSet tools = toolSet.Clone();
        tools.AddCollections([new BackgroundTasksToolCollection(background)]);
        return tools;
    }

    private ChatClientAgentOptions CreateOptions(ToolSet tools)
    {
        string prompt = sections.Compose(defaults, tools.Collections);
        List<AITool> builtTools = tools.Build();

        // Fresh objects per build, so no two agents and no caller list share state with the builder.
        ChatOptions chatOptions = new()
        {
            Instructions = prompt,
            Tools = builtTools.Count == 0 ? null : builtTools
        };
        foreach (Action<ChatOptions> configure in chatOptionsCallbacks)
        {
            configure(chatOptions);
        }

        return new ChatClientAgentOptions
        {
            Id = id,
            Name = name,
            Description = description,
            ChatOptions = chatOptions,
            ChatHistoryProvider = chatHistoryProvider,
            AIContextProviders = contextProviders.Count == 0 ? null : [.. contextProviders]
        };
    }

    private static string Require(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }

    private AgentBuilder Set(ref string? field, string value, string paramName)
    {
        field = Require(value, paramName);
        return this;
    }

    private AgentBuilder Add(List<string> list, string item, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item, paramName);
        list.Add(item);
        return this;
    }

    private AgentBuilder AddAll(List<string> list, IEnumerable<string> items, string paramName)
    {
        ArgumentNullException.ThrowIfNull(items, paramName);
        string[] copy = [.. items];
        foreach (string item in copy)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(item, paramName);
        }
        list.AddRange(copy);
        return this;
    }

    private AgentBuilder AddCallback<T>(List<Action<T>> list, Action<T> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        list.Add(configure);
        return this;
    }
}
