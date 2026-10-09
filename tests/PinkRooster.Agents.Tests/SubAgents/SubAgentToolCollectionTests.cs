using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Eventing;
using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.SubAgents;

namespace PinkRooster.Agents.Tests.SubAgents;

public sealed class SubAgentToolCollectionTests
{
    private static ChatMessage Call(string id, string name, Dictionary<string, object?>? arguments = null) =>
        new(ChatRole.Assistant, [new FunctionCallContent(id, name, arguments ?? [])]);

    private static ChatMessage Text(string text) => new(ChatRole.Assistant, text);

    // A client with no responses: its first request throws, which a test that must not reach the model relies on.
    private static ScriptedChatClient Unscripted() => new(Array.Empty<ChatMessage>());

    private static SubAgentModel Model(string name, IChatClient client) => new(name, $"Use {name} for tests.", client);

    [Fact]
    public async Task Call_RunsOnTheNamedModelOnly_WithTheBriefAsSystemPromptAndThePromptAsTheOnlyUserMessage()
    {
        ScriptedChatClient fast = new(Text("found it"));
        ScriptedChatClient strong = Unscripted();
        SubAgentToolCollection collection = new([Model("fast", fast), Model("strong", strong)]);

        string result = await collection.RunSubAgent("FAST", "Finder", "You find tests.", "Find the tests of Foo.",
            instructions: ["Read before you judge.", "Quote paths."], outputFormat: "Three bullets.");

        Assert.Equal("found it", result);
        Assert.Empty(strong.Requests);
        string prompt = fast.Options.Single()!.Instructions!;
        Assert.Contains("# Role\nYou find tests.", prompt.ReplaceLineEndings("\n"));
        Assert.Contains("- Read before you judge.", prompt);
        Assert.Contains("- Quote paths.", prompt);
        Assert.Contains("# Output Format\nThree bullets.", prompt.ReplaceLineEndings("\n"));
        ChatMessage only = Assert.Single(fast.Requests.Single());
        Assert.Equal((ChatRole.User, "Find the tests of Foo."), (only.Role, only.Text));
        Assert.Null(fast.Options.Single()!.Tools);
    }

    [Fact]
    public async Task Call_WithoutInstructionsOrOutputFormat_LeavesThoseSectionsToTheDefaults()
    {
        ScriptedChatClient model = new(Text("ok"));
        SubAgentToolCollection collection = new([Model("fast", model)], configure: builder => builder.WithoutDefaults());

        await collection.RunSubAgent("fast", "Helper", "You help.", "Do it.", instructions: [], outputFormat: " ");

        Assert.Equal("# Role\nYou help.", model.Options.Single()!.Instructions!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Call_NamingTwoToolSets_GivesTheToolsOfBothAndNoOthers()
    {
        ScriptedChatClient model = new(Text("ok"));
        AIFunction shared = AIFunctionFactory.Create(() => "s", "Shared");
        SubAgentToolCollection collection = new([Model("fast", model)],
        [
            new SubAgentToolSet("read", "Reading.") { Tools = [AIFunctionFactory.Create(() => "r", "Read"), shared] },
            new SubAgentToolSet("search", "Searching.") { Tools = [AIFunctionFactory.Create(() => "f", "Find"), shared] },
            new SubAgentToolSet("write", "Writing.") { Tools = [AIFunctionFactory.Create(() => "w", "Write")] }
        ]);

        await collection.RunSubAgent("fast", "Helper", "r", "p", toolSets: ["read", "SEARCH", "read"]);

        Assert.Equal(["Find", "Read", "Shared"], model.Options.Single()!.Tools!.Select(tool => tool.Name).Order());
    }

    [Fact]
    public async Task SecondCall_DoesNotSeeTheFirstCallsMessages()
    {
        ScriptedChatClient model = new(Text("one"), Text("two"));
        SubAgentToolCollection collection = new([Model("fast", model)]);

        await collection.RunSubAgent("fast", "First", "r", "first task");
        string second = await collection.RunSubAgent("fast", "Second", "r", "second task");

        Assert.Equal("two", second);
        Assert.Equal("second task", Assert.Single(model.Requests[1]).Text);
    }

    [Fact]
    public async Task SubAgentThatCallsTools_ReturnsOnlyItsFinalText()
    {
        ScriptedChatClient model = new(Call("c1", "Read"), Text("the summary"));
        SubAgentToolCollection collection = new([Model("fast", model)],
            [new SubAgentToolSet("read", "Reading.") { Tools = [AIFunctionFactory.Create(() => "a very long file", "Read")] }]);

        string result = await collection.RunSubAgent("fast", "Helper", "r", "p", toolSets: ["read"]);

        Assert.Equal("the summary", result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubAgentEvents_ReachTheCallingAgentsHandlersOnce_MarkedWithTheCallingRun(bool streaming)
    {
        List<AgentEvent> events = [];
        SubAgentToolCollection collection = new([Model("fast", new ScriptedChatClient(Text("inner answer")))]);
        ScriptedChatClient outerModel = new(
            Call("c1", "RunSubAgent", new() { ["modelName"] = "fast", ["subAgentName"] = "Helper", ["role"] = "You help.", ["prompt"] = "Do it." }),
            Text("done"));
        AIAgent outer = outerModel.CreateAgent().WithRole("outer").WithName("Main").WithTools(collection).OnEvent(events.Add).Build();

        if (streaming)
        {
            await foreach (AgentResponseUpdate _ in outer.RunStreamingAsync("go"))
            {
            }
        }
        else
        {
            await outer.RunAsync("go");
        }

        Guid outerRun = events.First(item => item.AgentName == "Main").RunId;
        AgentEvent[] nested = [.. events.Where(item => item.AgentName == "Helper")];
        Assert.Equal([typeof(RunStarted), typeof(ModelCallStarted), typeof(AssistantTextDelta), typeof(AssistantTextCompleted), typeof(ModelCallCompleted), typeof(RunCompleted)], nested.Select(item => item.GetType()));
        Assert.All(nested, item => Assert.Equal(outerRun, item.ParentRunId));
        Assert.Equal("inner answer", events.OfType<ToolCallCompleted>().Single(item => item.AgentName == "Main").Result?.ToString());
    }

    [Fact]
    public async Task Configure_RunsOnEverySubAgentsBuilder_AfterTheCollectionsOwnSettings()
    {
        ScriptedChatClient model = new(Text("one"), Text("two"));
        List<string> seen = [];
        SubAgentToolCollection collection = new([Model("fast", model)], configure: builder =>
        {
            seen.Add(builder.BuildOptions().Name!);
            builder.WithRole("The host's role.");
        });

        await collection.RunSubAgent("fast", "First", "The model's role.", "p");
        await collection.RunSubAgent("fast", "Second", "The model's role.", "p");

        Assert.Equal(["First", "Second"], seen);
        Assert.All(model.Options, options => Assert.Contains("The host's role.", options!.Instructions));
        Assert.All(model.Options, options => Assert.DoesNotContain("The model's role.", options!.Instructions));
    }

    [Fact]
    public async Task CancelledCall_Throws()
    {
        SubAgentToolCollection collection = new([Model("fast", new ScriptedChatClient(Text("never")))]);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection.RunSubAgent("fast", "Helper", "r", "p", cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task CancellingDuringTheRun_StopsTheSubAgentAndThrows()
    {
        using CancellationTokenSource source = new();
        int toolCalls = 0;
        AIFunction cancel = AIFunctionFactory.Create(() =>
        {
            toolCalls++;
            source.Cancel();
            return "cancelled";
        }, "Cancel");
        ScriptedChatClient model = new(Call("c1", "Cancel"), Text("never"));
        SubAgentToolCollection collection = new([Model("fast", model)], [new SubAgentToolSet("stop", "Stopping.") { Tools = [cancel] }]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection.RunSubAgent("fast", "Helper", "r", "p", toolSets: ["stop"], cancellationToken: source.Token));

        Assert.Equal(1, toolCalls);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task ResultOverTheLimit_IsCutInTheMiddleWithTheOmittedCount()
    {
        string answer = new string('x', 500) + new string('z', 500);
        SubAgentToolCollection collection = new([Model("fast", new ScriptedChatClient(Text(answer)))], maxResultCharacters: 200);

        string result = await collection.RunSubAgent("fast", "Helper", "r", "p");

        Assert.True(result.Length <= 200, $"The result has {result.Length} characters.");
        Assert.StartsWith("xxx", result);
        Assert.EndsWith("zzz", result);
        Assert.Matches(@"\[\.\.\. \d+ characters omitted \.\.\.\]", result);
        int kept = result.Count(character => character is 'x' or 'z');
        Assert.Contains($"[... {1000 - kept} characters omitted ...]", result);
    }

    [Fact]
    public async Task ResultWithinTheLimit_IsReturnedWhole()
    {
        string answer = new('a', 200);
        SubAgentToolCollection collection = new([Model("fast", new ScriptedChatClient(Text(answer)))], maxResultCharacters: 200);

        Assert.Equal(answer, await collection.RunSubAgent("fast", "Helper", "r", "p"));
    }

    [Theory]
    [InlineData("slow", "Helper", "r", "p", "Unknown model 'slow'. The models are: 'fast', 'strong'.")]
    [InlineData("fast", " ", "r", "p", "subAgentName is blank")]
    [InlineData("fast", "Helper", " ", "p", "role is blank")]
    [InlineData("fast", "Helper", "r", "", "prompt is blank")]
    public async Task RefusedCall_ReturnsAnErrorThatNamesTheFix_AndRunsNothing(string modelName, string subAgentName, string role, string prompt, string expected)
    {
        ScriptedChatClient fast = new(Text("never"));
        SubAgentToolCollection collection = new([Model("fast", fast), Model("strong", Unscripted())]);

        string result = await collection.RunSubAgent(modelName, subAgentName, role, prompt);

        Assert.StartsWith("Error: ", result);
        Assert.Contains(expected, result);
        Assert.Empty(fast.Requests);
    }

    [Fact]
    public async Task BlankInstruction_ReturnsAnError_AndRunsNothing()
    {
        ScriptedChatClient fast = new(Text("never"));
        SubAgentToolCollection collection = new([Model("fast", fast)]);

        string result = await collection.RunSubAgent("fast", "Helper", "r", "p", instructions: ["Fine.", " "]);

        Assert.StartsWith("Error: instructions holds a blank item", result);
        Assert.Empty(fast.Requests);
    }

    [Fact]
    public async Task UnknownToolSet_ReturnsAnErrorListingTheToolSets_AndRunsNothing()
    {
        ScriptedChatClient fast = new(Text("never"));
        SubAgentToolCollection withSets = new([Model("fast", fast)], [new SubAgentToolSet("read", "Reading.") { Tools = [AIFunctionFactory.Create(() => "r", "Read")] }]);
        SubAgentToolCollection withoutSets = new([Model("fast", fast)]);

        Assert.Equal("Error: Unknown tool set 'shell'. The tool sets are: 'read'.", await withSets.RunSubAgent("fast", "Helper", "r", "p", toolSets: ["shell"]));
        Assert.Equal("Error: Unknown tool set 'shell'. There are no tool sets; leave toolSets out.", await withoutSets.RunSubAgent("fast", "Helper", "r", "p", toolSets: ["shell"]));
        Assert.Empty(fast.Requests);
    }

    [Fact]
    public async Task NamedSetsWhoseToolsShareAName_ReturnTheBuildersErrorWithTheSetsNamed_AndRunNothing()
    {
        ScriptedChatClient fast = new(Text("never"));
        SubAgentToolCollection collection = new([Model("fast", fast)],
        [
            new SubAgentToolSet("one", "One.") { Tools = [AIFunctionFactory.Create(() => "1", "Same")] },
            new SubAgentToolSet("two", "Two.") { Tools = [AIFunctionFactory.Create(() => "2", "Same")] }
        ]);

        string result = await collection.RunSubAgent("fast", "Helper", "r", "p", toolSets: ["one", "two"]);

        Assert.StartsWith("Error: The sub-agent 'Helper' could not be built with the tool sets 'one', 'two': ", result);
        Assert.Contains("Same", result);
        Assert.Empty(fast.Requests);
    }

    [Fact]
    public async Task ThrowingConfigure_ReturnsItsMessageAsTheError()
    {
        SubAgentToolCollection collection = new([Model("fast", new ScriptedChatClient(Text("never")))], configure: _ => throw new InvalidOperationException("host broke"));

        Assert.Equal("Error: The sub-agent 'Helper' could not be built: host broke", await collection.RunSubAgent("fast", "Helper", "r", "p"));
    }

    [Fact]
    public async Task FailingModelCall_ReturnsAnErrorWithTheMessage_AndDoesNotThrow()
    {
        SubAgentToolCollection collection = new([Model("fast", Unscripted())]);

        string result = await collection.RunSubAgent("fast", "Helper", "r", "p");

        Assert.StartsWith("Error: The sub-agent 'Helper' failed: The script has 0 responses", result);
    }

    [Fact]
    public async Task ASubAgentThatFails_IsLoggedThroughTheBuildersLoggerFactory()
    {
        RecordingLoggerFactory loggers = new();
        SubAgentToolCollection collection = new SubAgentToolCollectionBuilder()
            .WithModel("fast", "Use fast for tests.", Unscripted())
            .WithLoggerFactory(loggers)
            .Build();

        string result = await collection.RunSubAgent("fast", "Helper", "r", "p", cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith("Error: The sub-agent 'Helper' failed", result);
        Assert.Contains(loggers.Logger.Errors, entry => entry.Message.Contains("Helper") && entry.Message.Contains("fast"));
    }

    [Fact]
    public async Task AnswerWithoutText_ReturnsTheNoAnswerError()
    {
        SubAgentToolCollection collection = new([Model("fast", new ScriptedChatClient(Text("  ")))]);

        Assert.StartsWith("Error: The sub-agent 'Helper' gave no answer.", await collection.RunSubAgent("fast", "Helper", "r", "p"));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(null, 0)]
    public async Task ToolThatNeedsApproval_RunsOnlyWhenTheCallbackApproves_AndTheRunGoesOnToAnAnswer(bool? answer, int expectedRuns)
    {
        int runs = 0;
        List<string> asked = [];
        AIFunction write = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => { runs++; return "written"; }, "Write"));
        ScriptedChatClient model = new(Call("c1", "Write"), Text("finished"));
        SubAgentToolCollection collection = new([Model("fast", model)],
            [new SubAgentToolSet("write", "Writing.") { Tools = [write] }],
            approveToolCall: answer is null ? null : (call, _) =>
            {
                asked.Add(call.Name);
                return Task.FromResult(new ApprovalAnswer(answer.Value));
            });

        string result = await collection.RunSubAgent("fast", "Helper", "r", "p", toolSets: ["write"]);

        Assert.Equal("finished", result);
        Assert.Equal(expectedRuns, runs);
        Assert.Equal(answer is null ? [] : ["Write"], asked);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task ApprovalCallback_IsAskedAboutOneCallAtATime_WhenSubAgentsRunAtOnce()
    {
        int active = 0;
        int most = 0;
        SubAgentToolCollection collection = new(
            [Model("one", new ScriptedChatClient(Call("c1", "Write"), Text("first"))), Model("two", new ScriptedChatClient(Call("c1", "Write"), Text("second")))],
            [new SubAgentToolSet("write", "Writing.") { Tools = [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => "written", "Write"))] }],
            approveToolCall: async (_, cancellationToken) =>
            {
                most = Math.Max(most, Interlocked.Increment(ref active));
                await Task.Delay(100, cancellationToken);
                Interlocked.Decrement(ref active);
                return ApprovalAnswer.Allow;
            });

        string[] results = await Task.WhenAll(
            collection.RunSubAgent("one", "First", "r", "p", toolSets: ["write"]),
            collection.RunSubAgent("two", "Second", "r", "p", toolSets: ["write"]));

        Assert.Equal(["first", "second"], results);
        Assert.Equal(1, most);
    }

    [Fact]
    public void Constructor_ListsEveryModelAndToolSetInOneInstruction()
    {
        SubAgentToolCollection collection = new(
            [new SubAgentModel("fast", "Searching and summarising.", Unscripted()), new SubAgentModel("strong", "Hard reasoning.", Unscripted())],
            [new SubAgentToolSet("files", "Reading the repository.") { Tools = [AIFunctionFactory.Create(() => "r", "Read")] }]);

        string instruction = Assert.Single(collection.Instructions);
        Assert.Contains("- fast: Searching and summarising.", instruction);
        Assert.Contains("- strong: Hard reasoning.", instruction);
        Assert.Contains("- files: Reading the repository.", instruction);
    }

    [Fact]
    public void Constructor_WithoutToolSets_DoesNotMentionThem()
    {
        SubAgentToolCollection collection = new([Model("fast", Unscripted())]);

        Assert.DoesNotContain("tool set", Assert.Single(collection.Instructions));
    }

    public static TheoryData<string, Func<SubAgentToolCollection>, string> BadConstructions()
    {
        IChatClient client = Unscripted();
        AITool tool = AIFunctionFactory.Create(() => "r", "Read");
        return new()
        {
            { "no model", () => new SubAgentToolCollection([]), "pass at least one SubAgentModel" },
            { "blank model name", () => new SubAgentToolCollection([new SubAgentModel(" ", "When.", client)]), "needs a Name and a UseWhen line" },
            { "blank model UseWhen", () => new SubAgentToolCollection([new SubAgentModel("fast", "", client)]), "needs a Name and a UseWhen line" },
            { "null client", () => new SubAgentToolCollection([new SubAgentModel("fast", "When.", null!)]), "The model 'fast' has no ChatClient" },
            { "repeated model", () => new SubAgentToolCollection([new SubAgentModel("fast", "When.", client), new SubAgentModel("FAST", "When.", client)]), "Two models are named 'FAST'" },
            { "blank tool set name", () => new SubAgentToolCollection([new SubAgentModel("fast", "When.", client)], [new SubAgentToolSet("", "When.") { Tools = [tool] }]), "Every SubAgentToolSet needs a Name and a UseWhen line" },
            { "empty tool set", () => new SubAgentToolCollection([new SubAgentModel("fast", "When.", client)], [new SubAgentToolSet("files", "When.")]), "The tool set 'files' holds no tool" },
            { "repeated tool set", () => new SubAgentToolCollection([new SubAgentModel("fast", "When.", client)], [new SubAgentToolSet("files", "When.") { Tools = [tool] }, new SubAgentToolSet("Files", "When.") { Tools = [tool] }]), "Two tool sets are named 'Files'" }
        };
    }

    [Theory]
    [MemberData(nameof(BadConstructions))]
    public void Constructor_RefusesABadList_NamingTheFix(string scenario, Func<SubAgentToolCollection> create, string expected)
    {
        ArgumentException thrown = Assert.Throws<ArgumentException>(create);

        Assert.True(thrown.Message.Contains(expected, StringComparison.Ordinal), $"{scenario}: {thrown.Message}");
    }

    [Fact]
    public void Constructor_RefusesAResultLimitBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubAgentToolCollection([Model("fast", Unscripted())], maxResultCharacters: 0));
    }

    [Fact]
    public void Tool_IsDescribedWellEnoughToSelectBy_AndSaysTheSubAgentSeesNothingElse()
    {
        AIFunction function = Assert.Single(new SubAgentToolCollection([Model("fast", Unscripted())]).GetAIFunctions());

        Assert.Equal("RunSubAgent", function.Name);
        Assert.IsNotType<ApprovalRequiredAIFunction>(function);
        Assert.True(function.Description.Length >= 60);
        Assert.Contains("sees nothing of this conversation", function.Description);
        JsonElement properties = function.JsonSchema.GetProperty("properties");
        Assert.Equal(["instructions", "modelName", "outputFormat", "prompt", "role", "subAgentName", "toolSets"], properties.EnumerateObject().Select(property => property.Name).Order());
        Assert.All(properties.EnumerateObject(), parameter => Assert.True(parameter.Value.GetProperty("description").GetString()!.Length > 10, parameter.Name));
        Assert.Equal(["modelName", "prompt", "role", "subAgentName"], function.JsonSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).Order());
    }
}
