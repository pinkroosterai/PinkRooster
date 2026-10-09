using Microsoft.Extensions.AI;
using PinkRooster.Agents.SubAgents;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.SubAgents;

public sealed class SubAgentToolCollectionBuilderTests
{
    private static ChatMessage Call(string id, string name) => new(ChatRole.Assistant, [new FunctionCallContent(id, name, new Dictionary<string, object?>())]);

    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    private static ScriptedChatClient Unscripted() => new(Array.Empty<ChatMessage>());

    private sealed class Clock : ToolCollection
    {
        [Tool("Now", "Tells the time.")]
        public string Now() => "noon";
    }

    [Fact]
    public async Task Build_GivesACollectionWithTheModelsAndEveryKindOfToolSet()
    {
        ScriptedChatClient small = new(Text("ok"));
        SubAgentToolCollection collection = new SubAgentToolCollectionBuilder()
            .WithModel("small", "Routine work.", small)
            .WithModel("large", "Hard work.", Unscripted())
            .WithToolSet("clock", "Telling the time.", new Clock())
            .WithToolSet("plain", "Plain tools.", [AIFunctionFactory.Create(() => "p", "Plain")])
            .WithToolSet(new SubAgentToolSet("mixed", "Both kinds.") { Tools = [AIFunctionFactory.Create(() => "m", "Mixed")] })
            .Build();

        string instruction = Assert.Single(collection.Instructions);
        Assert.Contains("- small: Routine work.", instruction);
        Assert.Contains("- large: Hard work.", instruction);
        Assert.Contains("- clock: Telling the time.", instruction);

        Assert.Equal("ok", await collection.RunSubAgent("small", "Helper", "r", "p", toolSets: ["clock", "plain", "mixed"]));
        Assert.Equal(["Mixed", "Now", "Plain"], small.Options.Single()!.Tools!.Select(tool => tool.Name).Order());
    }

    [Fact]
    public async Task WithMaxResultCharacters_SetsTheLimit()
    {
        SubAgentToolCollection collection = new SubAgentToolCollectionBuilder()
            .WithModel("small", "Routine work.", new ScriptedChatClient(Text(new string('x', 1000))))
            .WithMaxResultCharacters(200)
            .Build();

        string result = await collection.RunSubAgent("small", "Helper", "r", "p");

        Assert.True(result.Length <= 200);
        Assert.Contains("characters omitted", result);
    }

    [Fact]
    public async Task ConfigureSubAgents_RunsEveryCallbackInTheOrderAdded()
    {
        ScriptedChatClient small = new(Text("ok"));
        SubAgentToolCollection collection = new SubAgentToolCollectionBuilder()
            .WithModel("small", "Routine work.", small)
            .ConfigureSubAgents(builder => builder.WithoutDefaults().WithRole("first"))
            .ConfigureSubAgents(builder => builder.WithRole("second"))
            .Build();

        await collection.RunSubAgent("small", "Helper", "the model's role", "p");

        Assert.Equal("# Role\nsecond", small.Options.Single()!.Instructions!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task ApproveToolCallsWith_AnswersASubAgentsApprovalRequests()
    {
        int runs = 0;
        AIFunction write = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => { runs++; return "written"; }, "Write"));
        SubAgentToolCollection collection = new SubAgentToolCollectionBuilder()
            .WithModel("small", "Routine work.", new ScriptedChatClient(Call("c1", "Write"), Text("finished")))
            .WithToolSet("write", "Writing.", [write])
            .ApproveToolCallsWith((_, _) => Task.FromResult(true))
            .Build();

        Assert.Equal("finished", await collection.RunSubAgent("small", "Helper", "r", "p", toolSets: ["write"]));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Build_CalledTwice_GivesIndependentCollections_AndLaterChangesReachOnlyLaterBuilds()
    {
        SubAgentToolCollectionBuilder builder = new SubAgentToolCollectionBuilder().WithModel("small", "Routine work.", Unscripted());

        SubAgentToolCollection first = builder.Build();
        SubAgentToolCollection second = builder.WithModel("large", "Hard work.", Unscripted()).Build();

        Assert.NotSame(first, second);
        Assert.DoesNotContain("large", Assert.Single(first.Instructions));
        Assert.Contains("large", Assert.Single(second.Instructions));
    }

    [Fact]
    public void Build_WithoutAModel_ThrowsNamingTheFix()
    {
        ArgumentException thrown = Assert.Throws<ArgumentException>(() => new SubAgentToolCollectionBuilder().Build());

        Assert.Contains("pass at least one SubAgentModel", thrown.Message);
    }

    [Fact]
    public void Build_WithAToolSetWithoutTools_ThrowsNamingTheSet()
    {
        SubAgentToolCollectionBuilder builder = new SubAgentToolCollectionBuilder().WithModel("small", "Routine work.", Unscripted()).WithToolSet("empty", "Nothing.");

        Assert.Contains("The tool set 'empty' holds no tool", Assert.Throws<ArgumentException>(builder.Build).Message);
    }

    [Fact]
    public void Methods_RefuseBlankAndNullArgumentsAtTheCall()
    {
        SubAgentToolCollectionBuilder builder = new();

        Assert.Throws<ArgumentException>(() => builder.WithModel(" ", "When.", Unscripted()));
        Assert.Throws<ArgumentException>(() => builder.WithModel("small", "", Unscripted()));
        Assert.Throws<ArgumentNullException>(() => builder.WithModel("small", "When.", null!));
        Assert.Throws<ArgumentException>(() => builder.WithToolSet(" ", "When.", new Clock()));
        Assert.Throws<ArgumentNullException>(() => builder.WithToolSet("set", "When.", (IEnumerable<AITool>)null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithToolSet(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxResultCharacters(0));
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureSubAgents(null!));
        Assert.Throws<ArgumentNullException>(() => builder.ApproveToolCallsWith((Func<FunctionCallContent, CancellationToken, Task<bool>>)null!));
    }
}
