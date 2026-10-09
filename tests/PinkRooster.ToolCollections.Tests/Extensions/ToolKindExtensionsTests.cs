using Microsoft.Extensions.AI;

namespace PinkRooster.ToolCollections.Tests;

public sealed class ToolKindExtensionsTests
{
    [Fact]
    public void GetKind_ReturnsTheKindAToolMethodDeclares_AndNoneForOneThatDeclaresNothing()
    {
        IReadOnlyList<AIFunction> functions = new KindTools().GetAIFunctions();

        Assert.Equal(ToolKind.Read, functions.Single(function => function.Name == "Look").GetKind());
        Assert.Equal(ToolKind.Execute, functions.Single(function => function.Name == "Run").GetKind());
        Assert.Equal(ToolKind.None, functions.Single(function => function.Name == "Unknown").GetKind());
    }

    [Fact]
    public void GetKind_OfAToolThatNeedsApproval_IsStillItsKind()
    {
        AIFunction change = new KindTools().GetAIFunctions().Single(function => function.Name == "Change");

        Assert.IsType<ApprovalRequiredAIFunction>(change);
        Assert.Equal(ToolKind.Edit, change.GetKind());
    }

    [Fact]
    public void GetKind_OfAToolMadeOutsideACollection_IsTheMethodsKind()
    {
        KindTools tools = new();

        Assert.Equal(ToolKind.Read, ToolCollection.CreateTool(tools.Look).GetKind());
        Assert.Equal(ToolKind.Edit, ToolCollection.CreateTools(tools).Single(function => function.Name == "Change").GetKind());
    }

    [Fact]
    public void GetKind_OfAFunctionFromElsewhere_IsNone()
    {
        Assert.Equal(ToolKind.None, AIFunctionFactory.Create(() => "x", "plain").GetKind());
    }

    [Fact]
    public async Task WithKind_GivesTheNamedToolsAKind_IgnoringCase_AndTheToolStillRuns()
    {
        ExternalToolCollection tickets = new ExternalToolCollection("Tickets", [
                AIFunctionFactory.Create(() => "found", "search"),
                AIFunctionFactory.Create(() => "closed", "close")])
            .WithKind(ToolKind.Read, "SEARCH");

        AIFunction search = tickets.GetAIFunctions().Single(function => function.Name == "search");
        Assert.Equal(ToolKind.Read, search.GetKind());
        Assert.Equal(ToolKind.None, tickets.GetAIFunctions().Single(function => function.Name == "close").GetKind());
        Assert.Equal("found", (await search.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken))?.ToString());
    }

    [Fact]
    public void WithKind_KeepsApproval_WhicheverIsSetFirst_AndALaterKindReplacesTheFirst()
    {
        ExternalToolCollection tools = new ExternalToolCollection("Files", [
                AIFunctionFactory.Create(() => "x", "write"),
                AIFunctionFactory.Create(() => "x", "delete"),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "x", "move"))])
            .RequireApproval("write")
            .WithKind(ToolKind.Read, "write", "delete", "move")
            .WithKind(ToolKind.Edit, "write", "delete", "move")
            .RequireApproval("delete");

        Assert.All(tools.GetAIFunctions(), function =>
        {
            Assert.IsType<ApprovalRequiredAIFunction>(function);
            Assert.Equal(ToolKind.Edit, function.GetKind());
        });
    }

    [Fact]
    public void WithKind_KeepsAFunctionsOwnProperties()
    {
        AIFunction tagged = AIFunctionFactory.Create(() => "x", new AIFunctionFactoryOptions { Name = "tagged", AdditionalProperties = new Dictionary<string, object?> { ["owner"] = "ops" } });

        ExternalToolCollection tools = new ExternalToolCollection("Ops", [tagged]).WithKind(ToolKind.Execute, "tagged");

        AIFunction function = Assert.Single(tools.GetAIFunctions());
        Assert.Equal("ops", function.AdditionalProperties["owner"]);
        Assert.Equal(ToolKind.Execute, function.GetKind());
    }

    [Fact]
    public void WithKind_NamingAToolThatIsNotThere_ThrowsListingTheTools()
    {
        ExternalToolCollection tools = new("Tickets", [AIFunctionFactory.Create(() => "x", "search")]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => tools.WithKind(ToolKind.Read, "find"));

        Assert.Contains("WithKind names 'find'", error.Message);
        Assert.Contains("'search'", error.Message);
    }

    [Fact]
    public void WithKind_AfterTheToolsWereRead_Throws()
    {
        ExternalToolCollection tools = new("Tickets", [AIFunctionFactory.Create(() => "x", "search")]);
        _ = tools.GetAIFunctions();

        Assert.Throws<InvalidOperationException>(() => tools.WithKind(ToolKind.Read, "search"));
    }

    private sealed class KindTools : ToolCollection
    {
        [Tool("Look", "Reads.", Kind = ToolKind.Read)]
        public string Look() => "looked";

        [Tool("Change", "Edits.", RequiresApproval = true, Kind = ToolKind.Edit)]
        public string Change() => "changed";

        [Tool("Run", "Executes.", Kind = ToolKind.Execute)]
        public static string Run() => "ran";

        [Tool("Unknown", "Declares nothing.")]
        public string Unknown() => "unknown";
    }
}
