using System.Reflection;
using PinkRooster.Agents;
using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.BuiltIn;

namespace PinkRooster.Repository.Tests;

/// <summary>
/// Every tool the packages ship declares what it does, so a host that decides by kind never meets a built-in tool it knows nothing
/// about. The tools are read from the assemblies: a new <c>[Tool]</c> method without a kind fails here.
/// </summary>
public sealed class BuiltInToolKindTests
{
    // The packages that ship [Tool] methods of their own: the ready-made collections, and the two collections in Agents.
    private static readonly Assembly[] Assemblies = [typeof(Workspace).Assembly, typeof(AgentBuilder).Assembly];

    private static readonly Dictionary<string, ToolKind> Shipped = ShippedTools();

    [Fact]
    public void EveryShippedTool_DeclaresAKind()
    {
        Assert.True(Shipped.Count >= 23, $"Found {Shipped.Count} tools; the reflection over the assemblies no longer finds them all.");

        Assert.Empty(Shipped.Where(tool => tool.Value == ToolKind.None).Select(tool => $"{tool.Key}: add Kind = ToolKind.<kind> to its [Tool]"));
    }

    [Theory]
    [InlineData(ToolKind.State, "TaskAdd", "TaskUpdate", "TaskRemove", "AskUserQuestion", "RunSubAgent", "CancelTask")]
    [InlineData(ToolKind.Edit, "CreateFile", "WriteFile", "EditFile", "MoveFile", "DeleteFile")]
    [InlineData(ToolKind.Execute, "RunShell")]
    public void TheToolsThatAreNotRead_AreExactlyThese(ToolKind kind, params string[] expected)
    {
        Assert.Equal(expected.Order(StringComparer.Ordinal), Shipped.Where(tool => tool.Value == kind).Select(tool => tool.Key).Order(StringComparer.Ordinal));
    }

    // Tool name to declared kind, for every [Tool] method on a ToolCollection in the assemblies, internal collections included.
    private static Dictionary<string, ToolKind> ShippedTools()
    {
        Dictionary<string, ToolKind> tools = [];
        foreach (Type type in Assemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => type.IsSubclassOf(typeof(ToolCollection))))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<ToolAttribute>() is ToolAttribute attribute)
                {
                    tools.Add(string.IsNullOrWhiteSpace(attribute.Name) ? method.Name : attribute.Name, attribute.Kind);
                }
            }
        }
        return tools;
    }
}
