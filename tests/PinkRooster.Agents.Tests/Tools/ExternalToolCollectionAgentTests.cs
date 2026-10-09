using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.Context;
using PinkRooster.ToolCollections.Tests;

namespace PinkRooster.Agents.Tests.Tools;

public sealed class ExternalToolCollectionAgentTests
{
    private const string Framing = ToolCollectionChatClient.FramingLine;

    [Fact]
    public async Task AnAgentGetsTheToolsTextAndContextOnEveryModelCallOfTheToolLoop()
    {
        int lookups = 0;
        int contextCalls = 0;
        ExternalToolCollection tickets = new ExternalToolCollection("Tickets", [
                AIFunctionFactory.Create(() => $"found {++lookups}", "lookup", "Finds a ticket."),
                AIFunctionFactory.Create(() => "closed", "close", "Closes a ticket.")])
            .WithInstruction("Ticket numbers look like T-123.")
            .WithConstraint("Never close a ticket you did not look up.")
            .WithContext(_ => ValueTask.FromResult<string?>($"## Tickets\nContext call {++contextCalls}"));
        ScriptedChatClient client = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "lookup", new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = client.CreateAgent().WithoutDefaults().WithRole("r").WithTools(tickets).Build();

        AgentResponse response = await agent.RunAsync("go");

        Assert.Equal("done", response.Text);
        Assert.Equal(1, lookups);
        Assert.All(client.Options, options => Assert.Equal(["lookup", "close"], options!.Tools!.Select(tool => tool.Name)));
        Assert.Contains("Ticket numbers look like T-123.", client.Options[0]!.Instructions);
        Assert.Contains("Never close a ticket you did not look up.", client.Options[0]!.Instructions);
        Assert.Equal($"{Framing}\n\n## Tickets\nContext call 1", client.Requests[0][^1].Text);
        Assert.Equal($"{Framing}\n\n## Tickets\nContext call 2", client.Requests[1][^1].Text);
        Assert.Equal(2, contextCalls);
    }

    [Fact]
    public async Task WithoutAContextDelegate_NoContextMessageIsSent()
    {
        ExternalToolCollection tools = new("Plain", [AIFunctionFactory.Create(() => "x", "x")]);
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = client.CreateAgent().WithoutDefaults().WithRole("r").WithTools(tools).Build();

        await agent.RunAsync("go");

        Assert.Equal("go", client.Requests[0][^1].Text);
        Assert.Null(await tools.GetContextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WithContext_ASecondDelegateReplacesTheFirst()
    {
        ExternalToolCollection tools = new ExternalToolCollection("Plain", [])
            .WithContext(_ => ValueTask.FromResult<string?>("first"))
            .WithContext(_ => ValueTask.FromResult<string?>("second"));

        Assert.Equal("second", await tools.GetContextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RequireApproval_MakesTheNamedToolWaitForTheHost()
    {
        int deletes = 0;
        ExternalToolCollection files = new ExternalToolCollection("Files", [AIFunctionFactory.Create(() => $"deleted {++deletes}", "delete")])
            .RequireApproval("DELETE");
        ScriptedChatClient client = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "delete", new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = client.CreateAgent().WithoutDefaults().WithRole("r").WithTools(files).Build();
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse asked = await agent.RunAsync("go", session);
        ToolApprovalRequestContent request = Assert.Single(asked.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Equal(0, deletes);

        AgentResponse approved = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);

        Assert.Equal(1, deletes);
        Assert.Equal("done", approved.Text);
    }

    [Fact]
    public void RequireApproval_WrapsOnlyTheNamedToolsAndKeepsToolsThatAlreadyNeedApproval()
    {
        AIFunction guarded = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "x", "guarded"));
        ExternalToolCollection tools = new ExternalToolCollection("Mixed", [
                guarded,
                AIFunctionFactory.Create(() => "x", "named"),
                AIFunctionFactory.Create(() => "x", "free")])
            .RequireApproval("named", "guarded");

        IReadOnlyList<AIFunction> functions = tools.GetAIFunctions();

        Assert.Same(guarded, functions[0]);
        Assert.IsType<ApprovalRequiredAIFunction>(functions[1]);
        Assert.IsNotType<ApprovalRequiredAIFunction>(functions[2]);
    }

    [Fact]
    public void RequireApproval_NamingAnUnknownToolThrowsListingTheTools()
    {
        ExternalToolCollection tools = new("GitHub", [AIFunctionFactory.Create(() => "x", "search")]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => tools.RequireApproval("merge"));

        Assert.Contains("'merge'", error.Message);
        Assert.Contains("'GitHub'", error.Message);
        Assert.Contains("'search'", error.Message);
    }

    [Fact]
    public void RequireApproval_AfterTheToolsWereReadThrowsNamingTheFix()
    {
        ExternalToolCollection tools = new("GitHub", [AIFunctionFactory.Create(() => "x", "search")]);
        _ = tools.GetAIFunctions();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => tools.RequireApproval("search"));

        Assert.Contains("before giving the collection to an agent", error.Message);
    }

    [Fact]
    public void Constructor_RejectsABlankNameANullListAndANullEntry()
    {
        Assert.Throws<ArgumentException>(() => new ExternalToolCollection(" ", []));
        Assert.Throws<ArgumentNullException>(() => new ExternalToolCollection("x", null!));
        ArgumentException nullEntry = Assert.Throws<ArgumentException>(() => new ExternalToolCollection("x", [null!]));
        Assert.Contains("non-null", nullEntry.Message);
    }

    [Fact]
    public void Constructor_WithTwoToolsOfOneNameThrowsNamingTheToolTheCollectionAndTheFix()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new ExternalToolCollection("GitHub", [AIFunctionFactory.Create(() => "x", "search"), AIFunctionFactory.Create(() => "y", "Search")]));

        Assert.Contains("'Search'", error.Message);
        Assert.Contains("'GitHub'", error.Message);
        Assert.Contains("different name", error.Message);
    }

    [Fact]
    public void AnEmptyToolListIsAllowed()
    {
        ExternalToolCollection tools = new ExternalToolCollection("Notes", []).WithInstruction("Notes are kept elsewhere.");

        Assert.Empty(tools.GetAIFunctions());
        Assert.Equal(["Notes are kept elsewhere."], tools.Instructions);
    }

    [Fact]
    public void WithInstructionAndConstraint_RejectBlankText()
    {
        ExternalToolCollection tools = new("Notes", []);

        Assert.Throws<ArgumentException>(() => tools.WithInstruction(" "));
        Assert.Throws<ArgumentException>(() => tools.WithConstraint(""));
    }

    [Fact]
    public void Build_WithTwoExternalCollectionsSharingAToolNameThrowsNamingBothByDisplayName()
    {
        AgentBuilder builder = new ScriptedChatClient(new ChatMessage(ChatRole.Assistant, "done")).CreateAgent()
            .WithRole("r")
            .WithTools(
                new ExternalToolCollection("GitHub", [AIFunctionFactory.Create(() => "x", "search")]),
                new ExternalToolCollection("Context7", [AIFunctionFactory.Create(() => "y", "search")]));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("collection 'GitHub'", error.Message);
        Assert.Contains("collection 'Context7'", error.Message);
        Assert.Contains("different name", error.Message);
    }

    [Fact]
    public void AContextProviderNamesTwoClashingExternalCollectionsByDisplayName()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new ToolCollectionContextProvider(
            new ExternalToolCollection("GitHub", [AIFunctionFactory.Create(() => "x", "search")]),
            new ExternalToolCollection("Context7", [AIFunctionFactory.Create(() => "y", "search")])));

        Assert.Contains("'GitHub' and 'Context7'", error.Message);
    }

    [Fact]
    public void ASubclassListsItsToolMethodsFirstThenItsAddedTools()
    {
        IReadOnlyList<AIFunction> functions = new MixedTools([AIFunctionFactory.Create(() => "x", "generated")]).GetAIFunctions();

        Assert.Equal(["Native", "generated"], functions.Select(function => function.Name));
    }

    [Fact]
    public void ASubclassWhoseAddedToolClashesWithAToolMethodThrowsOnRead()
    {
        MixedTools tools = new([AIFunctionFactory.Create(() => "x", "native")]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => tools.GetAIFunctions());

        Assert.Contains("'native'", error.Message);
        Assert.Contains(nameof(MixedTools), error.Message);
        Assert.Contains("[Tool(", error.Message);
    }

    [Fact]
    public void AddTools_AfterTheToolsWereReadThrowsNamingTheFix()
    {
        MixedTools tools = new([]);
        _ = tools.GetAIFunctions();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => tools.AddLater(AIFunctionFactory.Create(() => "x", "late")));

        Assert.Contains("call it from the constructor", error.Message);
    }

    [Fact]
    public void AHandWrittenCollectionIsNamedByItsType()
    {
        Assert.Equal(nameof(TestCollections.Counter), new TestCollections.Counter().DisplayName);
    }

    private sealed class MixedTools : ToolCollection
    {
        public MixedTools(IEnumerable<AIFunction> generated) => AddTools(generated);

        [Tool("Native", "A hand-written tool.")]
        public string Native() => "native";

        public void AddLater(AIFunction function) => AddTools([function]);
    }
}
