using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections.Tests;

public sealed class ToolCollectionTests
{
    [Fact]
    public void GetAIFunctions_ExposesOnlyMarkedMethodsUnderTheirResolvedNames()
    {
        IReadOnlyList<AIFunction> functions = new SampleTools().GetAIFunctions();

        Assert.Equal(["Echo", "Static", "renamed_tool"], functions.Select(function => function.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void GetAIFunctions_PrefersToolAttributeDescriptionOverDescriptionAttribute()
    {
        AIFunction function = Single(new SampleTools(), "renamed_tool");

        Assert.Equal("From the tool attribute.", function.Description);
    }

    [Fact]
    public void GetAIFunctions_FallsBackToDescriptionAttribute()
    {
        AIFunction function = Single(new SampleTools(), "Echo");

        Assert.Equal("From the description attribute.", function.Description);
    }

    [Fact]
    public async Task GetAIFunctions_BindsInstanceMethodsToTheCollection()
    {
        AIFunction function = Single(new SampleTools("bound"), "Echo");

        object? result = await function.InvokeAsync(new AIFunctionArguments { ["value"] = "x" });

        Assert.Equal("bound:x", result?.ToString());
    }

    [Fact]
    public async Task GetAIFunctions_InvokesStaticMethods()
    {
        AIFunction function = Single(new SampleTools(), "Static");

        object? result = await function.InvokeAsync(new AIFunctionArguments());

        Assert.Equal("static", result?.ToString());
    }

    [Fact]
    public void GetAIFunctions_RejectsNamesThatDifferOnlyInCase()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new DuplicateTools().GetAIFunctions());

        Assert.Contains("lookup", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAIFunctions_RejectsAToolAttributeOnAPrivateMethod_NamingTheMethodAndTheFix()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new PrivateTool().GetAIFunctions());

        Assert.Contains("'PrivateTool.Hidden'", error.Message);
        Assert.Contains("private", error.Message);
        Assert.Contains("Make the method public", error.Message);
    }

    [Fact]
    public void GetAIFunctions_RejectsAToolAttributeOnANonPublicMethodOfABaseType()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new DerivedFromInternalTool().GetAIFunctions());

        Assert.Contains("'InternalToolBase.Hidden'", error.Message);
        Assert.Contains("internal", error.Message);
    }

    [Fact]
    public void SessionState_OutsideARun_ThrowsNamingTheCollectionAndTheFix()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new Counting().Next());

        Assert.Contains("'Counting'", error.Message);
        Assert.Contains("no run is in progress", error.Message);
        Assert.Contains("SessionState(session)", error.Message);
    }

    [Fact]
    public void CreateTools_MakesAToolOfEveryMarkedPublicMethodOfAnyObject_BoundToIt()
    {
        IReadOnlyList<AIFunction> tools = ToolCollection.CreateTools(new PlainObject());

        Assert.Equal(["Greet"], tools.Select(tool => tool.Name));
        Assert.Equal("A plain object's tool.", tools[0].Description);
    }

    [Fact]
    public void CreateTool_NeedsAToolAttribute_AndNamesTheFix()
    {
        AIFunction tool = ToolCollection.CreateTool(new PlainObject().Greet);
        ArgumentException error = Assert.Throws<ArgumentException>(() => ToolCollection.CreateTool(new PlainObject().Unmarked));

        Assert.Equal("Greet", tool.Name);
        Assert.Contains("mark it with [Tool]", error.Message);
    }

    [Fact]
    public void GetAIFunctions_BuildsTheFunctionsOncePerInstance()
    {
        SampleTools tools = new();

        Assert.Same(tools.GetAIFunctions(), tools.GetAIFunctions());
    }

    [Fact]
    public void GetAIFunctions_WrapsToolsThatRequireApproval()
    {
        IReadOnlyList<AIFunction> functions = new GuardedTools().GetAIFunctions();

        Assert.IsType<ApprovalRequiredAIFunction>(Assert.Single(functions, function => function.Name == "Delete"));
        Assert.IsNotType<ApprovalRequiredAIFunction>(Assert.Single(functions, function => function.Name == "Read"));
    }

    private static AIFunction Single(ToolCollection collection, string name) =>
        Assert.Single(collection.GetAIFunctions(), function => function.Name == name);

    private sealed class SampleTools(string prefix = "") : ToolCollection
    {
        [Tool]
        [Description("From the description attribute.")]
        public string Echo(string value) => $"{prefix}:{value}";

        [Tool("renamed_tool", "From the tool attribute.")]
        [Description("Ignored.")]
        public string Renamed() => "renamed";

        [Tool]
        public static string Static() => "static";

        public string NotATool() => "hidden";
    }

    private sealed class PrivateTool : ToolCollection
    {
        [Tool("Shown", "A public tool.")]
        public string Shown() => "shown";

        [Tool("Hidden", "Marked, but private.")]
        private string Hidden() => "hidden";
    }

    private abstract class InternalToolBase : ToolCollection
    {
        [Tool("Hidden", "Marked, but internal.")]
        internal static string Hidden() => "hidden";
    }

    private sealed class DerivedFromInternalTool : InternalToolBase
    {
        [Tool("Shown", "A public tool.")]
        public string Shown() => "shown";
    }

    private sealed class Counting : ToolCollection
    {
        private sealed class Count
        {
            public int Value { get; set; }
        }

        [Tool("Next", "Counts within the session.")]
        public int Next() => ++SessionState(() => new Count()).Value;
    }

    private sealed class PlainObject
    {
        [Tool("Greet", "A plain object's tool.")]
        public string Greet() => "hello";

        public string Unmarked() => "no";
    }

    private sealed class DuplicateTools : ToolCollection
    {
        [Tool("lookup", "First.")]
        public string First() => "first";

        [Tool("Lookup", "Second.")]
        public string Second() => "second";
    }

    private sealed class GuardedTools : ToolCollection
    {
        [Tool("Delete", "Deletes the file.", RequiresApproval = true)]
        public string Delete() => "deleted";

        [Tool("Read", "Reads the file.")]
        public string Read() => "read";
    }
}
