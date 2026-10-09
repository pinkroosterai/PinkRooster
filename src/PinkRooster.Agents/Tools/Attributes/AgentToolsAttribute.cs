// Declared in the package's root namespace by hand, like CreateAgent: an agent class needs the root using alone for its attributes.
namespace PinkRooster.Agents;

/// <summary>Gives a <see cref="DeclaredAgent"/> class tool collections, each created through its public parameterless constructor.</summary>
/// <remarks>
/// <para>
/// A class may hold several. On a class hierarchy the base types' collections come first, then the derived class's. The order between several of
/// these attributes on one class isn't guaranteed, so list collections whose order matters in one attribute.
/// </para>
/// <para>
/// Every instance of the class gets its own collection objects. A collection that needs constructor arguments is added with
/// <c>WithTools</c> in <c>Configure</c> instead. The collections are added after the class's own tools and before <c>Configure</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [AgentRole("You are a build assistant.")]
/// [AgentTools(typeof(ShellToolCollection), typeof(DateTimeToolCollection))]
/// public sealed class BuildAgent(IChatClient chatClient) : DeclaredAgent(chatClient);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
public sealed class AgentToolsAttribute : Attribute
{
    /// <summary>The collection types, in order.</summary>
    public Type[] Collections { get; }

    /// <param name="collections">One or more types derived from <c>ToolCollection</c>, each with a public parameterless constructor.</param>
    public AgentToolsAttribute(params Type[] collections)
    {
        Collections = collections ?? [];
    }
}
