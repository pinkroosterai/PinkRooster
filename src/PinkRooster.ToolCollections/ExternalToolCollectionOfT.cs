using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections;

/// <summary>
/// Base of a tool collection made from functions defined elsewhere, such as an MCP server's tools or generated ones, given the same
/// standing instructions, constraints, approvals, kinds and live context as a hand-written <see cref="ToolCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// The tool list is fixed when the collection is created. When the source changes its tools, create a new collection and a new
/// agent. The collection does not own the source: keep it alive, and dispose it, yourself.
/// </para>
/// <para>
/// Set everything before giving the collection to an agent. An agent reads the instructions and constraints when it is built,
/// and <see cref="RequireApproval"/> and <see cref="WithKind"/> throw once the tools have been read.
/// </para>
/// <para>
/// Derive from this type, naming your own class as <typeparamref name="TSelf"/>, for a collection with members of its own: the fluent
/// methods then return your class, so a chain keeps those members. <see cref="ExternalToolCollection"/> is the ready-made one.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The deriving class itself, which the fluent methods return.</typeparam>
public abstract class ExternalToolCollection<TSelf> : ToolCollection where TSelf : ExternalToolCollection<TSelf>
{
    private readonly string name;
    private Func<CancellationToken, ValueTask<string?>>? context;

    /// <param name="name">How messages name the collection, such as the server it came from.</param>
    /// <param name="tools">The tools, in the order the model sees them. Filter or rename them before passing them in.</param>
    /// <exception cref="ArgumentException">The name is blank, the list holds a null entry, or two tools share a name, ignoring case.</exception>
    protected ExternalToolCollection(string name, IEnumerable<AIFunction> tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.name = name;
        AddTools(tools);
    }

    /// <inheritdoc />
    public override string DisplayName => name;

    /// <summary>Adds an instruction an agent given this collection adds to its system prompt.</summary>
    /// <returns>This collection, for chaining.</returns>
    public TSelf WithInstruction(string instruction)
    {
        AddInstruction(instruction);
        return (TSelf)this;
    }

    /// <summary>Adds a constraint an agent given this collection adds to its system prompt.</summary>
    /// <returns>This collection, for chaining.</returns>
    public TSelf WithConstraint(string constraint)
    {
        AddConstraint(constraint);
        return (TSelf)this;
    }

    /// <summary>Makes the named tools need the host's approval before they run. Names are matched ignoring case.</summary>
    /// <returns>This collection, for chaining.</returns>
    /// <exception cref="ArgumentException">A name matches no tool; the message lists the tools there are.</exception>
    /// <exception cref="InvalidOperationException">The collection was already given to an agent.</exception>
    public TSelf RequireApproval(params string[] toolNames)
    {
        RequireApprovalForAddedTools(toolNames);
        return (TSelf)this;
    }

    /// <summary>
    /// Gives the named tools a <see cref="ToolKind"/>, which a host reads with <see cref="ToolKindExtensions.GetKind"/>; a function
    /// from elsewhere declares none. Names are matched ignoring case, and a later call for the same tool replaces the kind.
    /// </summary>
    /// <returns>This collection, for chaining.</returns>
    /// <exception cref="ArgumentException">A name matches no tool; the message lists the tools there are.</exception>
    /// <exception cref="InvalidOperationException">The collection was already given to an agent.</exception>
    public TSelf WithKind(ToolKind kind, params string[] toolNames)
    {
        SetKindOfAddedTools(kind, toolNames);
        return (TSelf)this;
    }

    /// <summary>Sets what the model should know about the tools' current state; replaces a delegate set earlier.</summary>
    /// <param name="context">Returns the text, or null for nothing. Its tokens are paid on every model call, so keep it short.</param>
    /// <returns>This collection, for chaining.</returns>
    public TSelf WithContext(Func<CancellationToken, ValueTask<string?>> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
        return (TSelf)this;
    }

    /// <inheritdoc />
    public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) =>
        context is null ? ValueTask.FromResult<string?>(null) : context(cancellationToken);
}
