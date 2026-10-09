using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.ToolCollections.Context;

namespace PinkRooster.ToolCollections;

/// <summary>
/// Abstract base class that scans implementing classes via reflection to expose marked methods as <see cref="AIFunction"/> tools,
/// together with standing prompt text and, optionally, the collection's current state.
/// </summary>
/// <remarks>
/// Give a collection to an agent with the agent builder's <c>WithTools(collection)</c>, or to any MAF agent through a
/// <see cref="ToolCollectionContextProvider"/>. An instance's state is shared by every agent and session it is given to.
/// </remarks>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
public abstract class ToolCollection
{
    private readonly List<string> addedInstructions = [];
    private readonly List<string> addedConstraints = [];
    private readonly List<AIFunction> addedFunctions = [];
    private readonly Lazy<IReadOnlyList<AIFunction>> functions;

    /// <summary>Initializes the collection.</summary>
    protected ToolCollection()
    {
        functions = new Lazy<IReadOnlyList<AIFunction>>(CreateAIFunctions);
    }

    /// <summary>How error and log messages name this collection. The type name, unless a derived type names itself.</summary>
    public virtual string DisplayName => GetType().Name;
    /// <summary>
    /// Instructions an agent given this collection adds to its system prompt: those of the class's
    /// <see cref="ToolCollectionInstructionAttribute"/>s, base types first, then those added with <see cref="AddInstruction"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">An attribute holds blank text.</exception>
    public IReadOnlyList<string> Instructions =>
        [.. ReadAttributeText<ToolCollectionInstructionAttribute>(attribute => attribute.Instructions), .. addedInstructions];

    /// <summary>
    /// Constraints an agent given this collection adds to its system prompt: those of the class's
    /// <see cref="ToolCollectionConstraintAttribute"/>s, base types first, then those added with <see cref="AddConstraint"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">An attribute holds blank text.</exception>
    public IReadOnlyList<string> Constraints =>
        [.. ReadAttributeText<ToolCollectionConstraintAttribute>(attribute => attribute.Constraints), .. addedConstraints];

    /// <summary>Adds an instruction that depends on how the collection was created, such as the shell it runs. Call it from the constructor.</summary>
    /// <remarks>An agent reads the list when it is built, so text added later reaches only agents built later.</remarks>
    protected void AddInstruction(string instruction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        addedInstructions.Add(instruction);
    }

    /// <summary>Adds a constraint that depends on how the collection was created, such as a host policy. Call it from the constructor.</summary>
    /// <remarks>An agent reads the list when it is built, so text added later reaches only agents built later.</remarks>
    protected void AddConstraint(string constraint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constraint);
        addedConstraints.Add(constraint);
    }

    /// <summary>
    /// Adds ready-made functions as tools, such as generated ones or an MCP server's, next to the <see cref="ToolAttribute"/> methods.
    /// Call it from the constructor. A function that is an <see cref="ApprovalRequiredAIFunction"/> keeps needing approval.
    /// </summary>
    /// <exception cref="ArgumentException">The list holds a null entry, or two functions with the same name, ignoring case.</exception>
    /// <exception cref="InvalidOperationException">The tool list was already read.</exception>
    protected void AddTools(IEnumerable<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ThrowIfToolsWereRead(nameof(AddTools));
        AIFunction[] copy = [.. tools];
        if (copy.Any(tool => tool is null))
        {
            throw new ArgumentException("Every tool must be non-null.", nameof(tools));
        }

        HashSet<string> names = new(addedFunctions.Select(function => function.Name), StringComparer.OrdinalIgnoreCase);
        foreach (AIFunction tool in copy)
        {
            if (!names.Add(tool.Name))
            {
                throw new ArgumentException(
                    $"Duplicate tool name '{tool.Name}' in '{DisplayName}'. Tool names must be unique, ignoring case; give one a different name before adding it.",
                    nameof(tools));
            }
        }
        addedFunctions.AddRange(copy);
    }

    /// <summary>Makes the named added tools need the host's approval. Call it before the tool list is read.</summary>
    /// <exception cref="ArgumentException">A name matches no added tool; the message lists the tools there are.</exception>
    /// <exception cref="InvalidOperationException">The tool list was already read.</exception>
    private protected void RequireApprovalForAddedTools(IEnumerable<string> toolNames)
    {
        string[] names = NamesOfAddedTools("RequireApproval", toolNames);
        for (int i = 0; i < addedFunctions.Count; i++)
        {
            AIFunction function = addedFunctions[i];
            if (function is not ApprovalRequiredAIFunction && names.Contains(function.Name, StringComparer.OrdinalIgnoreCase))
            {
                addedFunctions[i] = new ApprovalRequiredAIFunction(function);
            }
        }
    }

    /// <summary>Gives the named added tools a <see cref="ToolKind"/>, replacing the one they had. Call it before the tool list is read.</summary>
    /// <exception cref="ArgumentException">A name matches no added tool; the message lists the tools there are.</exception>
    /// <exception cref="InvalidOperationException">The tool list was already read.</exception>
    private protected void SetKindOfAddedTools(ToolKind kind, IEnumerable<string> toolNames)
    {
        string[] names = NamesOfAddedTools("WithKind", toolNames);
        for (int i = 0; i < addedFunctions.Count; i++)
        {
            AIFunction function = addedFunctions[i];
            if (names.Contains(function.Name, StringComparer.OrdinalIgnoreCase))
            {
                // The tool loop recognises approval by its type, so that wrapper goes back on around the kind.
                KindedFunction kinded = new(function, kind);
                addedFunctions[i] = function is ApprovalRequiredAIFunction ? new ApprovalRequiredAIFunction(kinded) : kinded;
            }
        }
    }

    // The names a fluent method was given, once each is known to be an added tool and the tool list is still open.
    private string[] NamesOfAddedTools(string member, IEnumerable<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        ThrowIfToolsWereRead(member);
        string[] names = [.. toolNames];
        foreach (string name in names)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(toolNames));
            if (!addedFunctions.Any(function => string.Equals(function.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                string known = addedFunctions.Count == 0 ? "none" : string.Join(", ", addedFunctions.Select(function => $"'{function.Name}'"));
                throw new ArgumentException($"{member} names '{name}', but '{DisplayName}' has no such tool. Its tools are: {known}.", nameof(toolNames));
            }
        }
        return names;
    }

    /// <summary>
    /// Returns an <see cref="AIFunction"/> for each public static or instance method marked with <see cref="ToolAttribute"/>, then
    /// the functions added with <see cref="AddTools"/>, built once per instance. A tool marked <see cref="ToolAttribute.RequiresApproval"/>
    /// is an <see cref="ApprovalRequiredAIFunction"/>.
    /// </summary>
    /// <returns>A read-only list of the collection's <see cref="AIFunction"/> tools.</returns>
    /// <exception cref="InvalidOperationException">Thrown when duplicate tool names are detected in the collection, or a method marked with <see cref="ToolAttribute"/> is not public.</exception>
    public IReadOnlyList<AIFunction> GetAIFunctions() => functions.Value;

    /// <summary>
    /// How to rename <paramref name="toolName"/> of this collection, for the agent builder's duplicate-name error; null gives the
    /// general advice. A collection whose tools are renamed where it is created, such as an MCP collection, says how here.
    /// </summary>
    /// <param name="toolName">The tool whose name clashes.</param>
    /// <returns>A clause that names the fix, such as <c>rename it in the select argument of ConnectAsync</c>, without a full stop; or null.</returns>
    public virtual string? RenameHint(string toolName) => null;

    private void ThrowIfToolsWereRead(string member)
    {
        if (functions.IsValueCreated)
        {
            throw new InvalidOperationException(
                $"{member} was called on '{DisplayName}' after its tools were read; call it from the constructor, or before giving the collection to an agent.");
        }
    }

    /// <summary>Returns what the model should know about this collection's current state, or null for nothing.</summary>
    /// <remarks>
    /// <para>
    /// An agent built with the agent builder asks every collection before each model call, including each call of the tool
    /// loop, and sends the text as one marked message after the rest of the request; a <see cref="ToolCollectionContextProvider"/>
    /// asks once per run. The text is never stored in the session's history.
    /// </para>
    /// <para>
    /// Its tokens are paid on every model call, so keep it short. Start it with your own markdown heading, such as
    /// <c>## Current To-Do list</c>. A thrown exception is logged and the collection left out of that model call. With no
    /// logger the failure is invisible and the model answers without this collection's state; give the agent a logger factory to see it.
    /// </para>
    /// </remarks>
    public virtual ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

    /// <summary>
    /// Returns this collection's state of type <typeparamref name="T"/> for the session of the run in progress, creating it with
    /// <paramref name="create"/> the first time. Call it from a tool method or from <see cref="GetContextAsync"/>.
    /// </summary>
    /// <remarks>
    /// One collection instance serves every session, so what belongs to one conversation lives in the session, never in a field. The
    /// state is kept in the session's <c>StateBag</c> under a key made of the collection's type and <typeparamref name="T"/>, so it
    /// travels with the session when that is saved and restored: keep it small and JSON-serializable. A change made on the returned
    /// object is kept for the rest of the session's life in this process; call <see cref="SetSessionState{T}(T)"/> after a change that must
    /// also survive saving and restoring the session, or to replace the state.
    /// </remarks>
    /// <typeparam name="T">The state's type; one state per type and collection type.</typeparam>
    /// <param name="create">Makes the state for a session that has none yet.</param>
    /// <exception cref="InvalidOperationException">No run is in progress, so there is no session to read; the message names the fix.</exception>
    protected T SessionState<T>(Func<T> create) where T : class
    {
        ArgumentNullException.ThrowIfNull(create);
        AgentSession session = CurrentSession();
        string key = SessionStateKey<T>();
        // Two tool calls of one model reply can run at the same time; both must get the same state.
        lock (session.StateBag)
        {
            if (!session.StateBag.TryGetValue(key, out T? state) || state is null)
            {
                state = create() ?? throw new InvalidOperationException($"The create callback given to SessionState<{typeof(T).Name}> on '{DisplayName}' returned null; return the new state.");
                session.StateBag.SetValue(key, state);
            }
            return state;
        }
    }

    /// <summary>Returns this collection's state of type <typeparamref name="T"/> in <paramref name="session"/>, or null when that session holds none. For a host that shows the state outside a run.</summary>
    /// <typeparam name="T">The state's type.</typeparam>
    /// <param name="session">The session to read.</param>
    protected T? SessionState<T>(AgentSession session) where T : class
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.StateBag.TryGetValue(SessionStateKey<T>(), out T? state) ? state : null;
    }

    /// <summary>Stores <paramref name="state"/> as this collection's state of type <typeparamref name="T"/> for the session of the run in progress, replacing what was there.</summary>
    /// <typeparam name="T">The state's type.</typeparam>
    /// <param name="state">The new state.</param>
    /// <exception cref="InvalidOperationException">No run is in progress, so there is no session to write; the message names the fix.</exception>
    protected void SetSessionState<T>(T state) where T : class
    {
        ArgumentNullException.ThrowIfNull(state);
        CurrentSession().StateBag.SetValue(SessionStateKey<T>(), state);
    }

    /// <summary>
    /// Stores <paramref name="state"/> as this collection's state of type <typeparamref name="T"/> in <paramref name="session"/>, replacing
    /// what was there. For a host call that changes the state outside a run, next to <see cref="SessionState{T}(AgentSession)"/>.
    /// </summary>
    /// <typeparam name="T">The state's type.</typeparam>
    /// <param name="session">The session to write.</param>
    /// <param name="state">The new state.</param>
    protected void SetSessionState<T>(AgentSession session, T state) where T : class
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(state);
        session.StateBag.SetValue(SessionStateKey<T>(), state);
    }

    private AgentSession CurrentSession() =>
        AIAgent.CurrentRunContext?.Session ?? throw new InvalidOperationException(
            $"'{DisplayName}' keeps this state in the session of the run in progress, and no run is in progress here. " +
            "Read it from a tool method or from GetContextAsync, which run inside an agent run; outside a run, pass the session to SessionState(session).");

    private string SessionStateKey<T>() => $"{GetType().FullName}:{typeof(T).FullName}";

    private IReadOnlyList<AIFunction> CreateAIFunctions()
    {
        Type currentType = GetType();
        ThrowIfToolMethodIsNotPublic(currentType);
        MethodInfo[] methods = currentType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        List<AIFunction> created = [];
        HashSet<string> toolNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (MethodInfo method in methods)
        {
            // Ignore methods declared directly on System.Object or ToolCollection itself
            if (method.DeclaringType == typeof(object) || method.DeclaringType == typeof(ToolCollection))
            {
                continue;
            }

            ToolAttribute? toolAttribute = method.GetCustomAttribute<ToolAttribute>();
            if (toolAttribute is null)
            {
                continue;
            }

            // Bind to 'this' instance for instance methods, or 'null' for static methods
            AIFunction function = CreateFunction(method, method.IsStatic ? null : this, toolAttribute);

            // Validate against duplicate tool names across overloading or explicit attributes
            if (!toolNames.Add(function.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate tool name '{function.Name}' found on type '{currentType.FullName}'. " +
                    "Tool names must be unique to avoid schema validation errors with LLM providers.");
            }

            created.Add(function);
        }

        foreach (AIFunction function in addedFunctions)
        {
            if (!toolNames.Add(function.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate tool name '{function.Name}' in '{DisplayName}': an added tool has the name of a [Tool] method. " +
                    "Rename the method with [Tool(\"<name>\", ...)], or give the added tool a different name.");
            }
            created.Add(function);
        }

        return created;
    }

    /// <summary>
    /// Throws for a <see cref="ToolAttribute"/> on a method of <paramref name="type"/> or a base type that is not public: only public methods
    /// become tools, and a marked method left out without a word is a tool the model silently lacks.
    /// </summary>
    /// <exception cref="InvalidOperationException">A marked method is not public; the message names it and the fix.</exception>
    internal static void ThrowIfToolMethodIsNotPublic(Type type)
    {
        // A base type's private methods are not listed on the derived type, so each type is read on its own.
        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (MethodInfo method in current.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<ToolAttribute>(inherit: false) is null)
                {
                    continue;
                }
                string access = method.IsPrivate ? "private" : method.IsFamily ? "protected" : method.IsAssembly ? "internal" : "not public";
                throw new InvalidOperationException(
                    $"[Tool] is on '{current.Name}.{method.Name}', which is {access}, and only public methods become tools. " +
                    "Make the method public, or remove the attribute.");
            }
        }
    }

    /// <summary>Reads the text of every <typeparamref name="TAttribute"/> on this collection's type and its base types, base types first; blank text throws naming the type.</summary>
    private IReadOnlyList<string> ReadAttributeText<TAttribute>(Func<TAttribute, string[]> text) where TAttribute : Attribute
    {
        Type type = GetType();
        List<Type> chain = [];
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            chain.Add(current);
        }
        // Reflection lists a derived type's attributes first; the general text of a base type reads better before the specific.
        chain.Reverse();
        string[] items = [.. chain.SelectMany(current => current.GetCustomAttributes<TAttribute>(inherit: false)).SelectMany(text)];
        if (items.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"A [{typeof(TAttribute).Name.Replace("Attribute", "")}] on '{type.FullName}' holds blank text; every entry must be non-blank.");
        }
        return items;
    }

    /// <summary>
    /// Makes a tool from a method marked with <see cref="ToolAttribute"/>, named, described and approval-marked the way a collection's
    /// own tool methods are. For a tool that lives outside a collection.
    /// </summary>
    /// <param name="method">The method, such as <c>clock.GetTime</c>; an instance method keeps its target.</param>
    /// <exception cref="ArgumentException">The method has no <see cref="ToolAttribute"/>; the message names the fix.</exception>
    public static AIFunction CreateTool(Delegate method)
    {
        ArgumentNullException.ThrowIfNull(method);
        ToolAttribute toolAttribute = method.Method.GetCustomAttribute<ToolAttribute>()
            ?? throw new ArgumentException($"Method '{method.Method.Name}' has no [Tool] attribute; mark it with [Tool], or make the tool with AIFunctionFactory.Create(method, name, description).", nameof(method));
        return CreateFunction(method, toolAttribute);
    }

    /// <summary>
    /// Makes a tool from every public method of <paramref name="target"/> marked with <see cref="ToolAttribute"/>, static ones included,
    /// the way a collection does for its own. For an object that is not a collection but carries tool methods, such as an agent class.
    /// </summary>
    /// <param name="target">The object whose methods become tools; instance methods run on it.</param>
    /// <returns>The tools, in the order reflection lists the methods; empty when no method is marked.</returns>
    /// <exception cref="InvalidOperationException">A marked method is not public; the message names it and the fix.</exception>
    public static IReadOnlyList<AIFunction> CreateTools(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Type type = target.GetType();
        ThrowIfToolMethodIsNotPublic(type);
        List<AIFunction> created = [];
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (method.GetCustomAttribute<ToolAttribute>() is ToolAttribute toolAttribute)
            {
                created.Add(CreateFunction(method, method.IsStatic ? null : target, toolAttribute));
            }
        }
        return created;
    }

    /// <summary>
    /// Makes the tool of a <see cref="ToolAttribute"/> method: named, described, given its <see cref="ToolAttribute.Kind"/> and, when <see cref="ToolAttribute.RequiresApproval"/> is set,
    /// wrapped in an <see cref="ApprovalRequiredAIFunction"/>. Every way a <c>[Tool]</c> method becomes a tool goes through here.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="target">The instance an instance method is bound to; null for a static method.</param>
    /// <param name="toolAttribute">The method's attribute.</param>
    internal static AIFunction CreateFunction(MethodInfo method, object? target, ToolAttribute toolAttribute) =>
        WithApproval(AIFunctionFactory.Create(method, target, CreateFactoryOptions(method, toolAttribute)), toolAttribute);

    /// <summary>As <see cref="CreateFunction(MethodInfo, object?, ToolAttribute)"/>, for a delegate, which keeps its own target.</summary>
    internal static AIFunction CreateFunction(Delegate method, ToolAttribute toolAttribute) =>
        WithApproval(AIFunctionFactory.Create(method, CreateFactoryOptions(method.Method, toolAttribute)), toolAttribute);

    private static AIFunction WithApproval(AIFunction function, ToolAttribute toolAttribute) =>
        toolAttribute.RequiresApproval ? new ApprovalRequiredAIFunction(function) : function;

    // The attribute's name, else the C# method name; the attribute's description, else null so AIFunctionFactory reads the method's DescriptionAttribute;
    // the attribute's kind, in the properties every wrapper passes on.
    private static AIFunctionFactoryOptions CreateFactoryOptions(MethodInfo method, ToolAttribute toolAttribute) => new()
    {
        Name = !string.IsNullOrWhiteSpace(toolAttribute.Name) ? toolAttribute.Name : method.Name,
        Description = string.IsNullOrWhiteSpace(toolAttribute.Description) ? null : toolAttribute.Description,
        AdditionalProperties = toolAttribute.Kind == ToolKind.None ? null : new Dictionary<string, object?> { [ToolKindExtensions.Key] = toolAttribute.Kind }
    };
}
