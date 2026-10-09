using System.ComponentModel;
using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PinkRooster.Agents.Prompts;

using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.Context;
using PinkRooster.ToolCollections.Tests;

namespace PinkRooster.Agents.Tests;

public sealed class AgentBuilderTests
{
    private const string Framing = ToolCollectionChatClient.FramingLine;

    [Fact]
    public async Task Build_RendersSectionsAsMarkdownHeadingsInOrderAndLeavesEmptySectionsOut()
    {
        RecordingChatClient client = new(_ => "reply");
        AIAgent agent = client.CreateAgent()
            .WithoutDefaults()
            .WithConstraint("Do not invent details")
            .WithInstructions(["Remove filler phrases", "Use abbreviations where possible"])
            .WithObjective("Shorten the text")
            .WithRole("summarizer")
            .Build();

        await agent.RunAsync("text");

        string expected = Normalize("""
            # Role
            summarizer

            # Objective
            Shorten the text

            # Instructions
            - Remove filler phrases
            - Use abbreviations where possible

            # Constraints
            - Do not invent details
            """);
        Assert.Equal(expected, Normalize(Assert.Single(client.Requests).Instructions));
    }

    [Fact]
    public async Task Build_RendersEveryOptionalSection()
    {
        RecordingChatClient client = new(_ => "reply");
        AIAgent agent = client.CreateAgent()
            .WithoutDefaults()
            .WithRole("r")
            .WithBackground("b")
            .WithOutputFormat("o")
            .WithExample("in 1", "out 1")
            .WithExample("in 2", "out 2")
            .Build();

        await agent.RunAsync("go");

        string expected = Normalize("""
            # Role
            r

            # Background
            b

            # Output Format
            o

            # Examples
            Example 1:
            Input: in 1
            Output: out 1

            Example 2:
            Input: in 2
            Output: out 2
            """);
        Assert.Equal(expected, Normalize(client.Requests[0].Instructions));
    }

    [Fact]
    public async Task RoleOnlyWithoutDefaults_SendsOnlyTheRoleAndNoTools()
    {
        RecordingChatClient client = new(_ => "reply");
        AIAgent agent = client.CreateAgent().WithoutDefaults().WithRole("summarizer").Build();

        await agent.RunAsync("go");

        RecordedRequest request = Assert.Single(client.Requests);
        Assert.Equal("# Role\nsummarizer", Normalize(request.Instructions));
        Assert.Empty(request.ToolNames);
    }

    [Fact]
    public async Task WithSystemPrompt_SendsThePromptExactlyAndNeedsNoRole()
    {
        RecordingChatClient client = new(_ => "reply");
        AIAgent agent = client.CreateAgent().WithSystemPrompt("x").Build();

        await agent.RunAsync("go");

        Assert.Equal("x", client.Requests[0].Instructions);
    }

    [Fact]
    public async Task Build_AddsCollectionTextAfterTheDefaultsAndBeforeTheAgentsOwnInTheOrderCollectionsWereAdded()
    {
        RecordingChatClient client = new(_ => "reply");
        AIAgent agent = client.CreateAgent()
            .WithDefaults(new AgentDefaults(["Default instruction."], ["Default constraint."]))
            .WithRole("r")
            .WithInstruction("Answer briefly.")
            .WithConstraint("Stay polite.")
            .WithTools(new ShellLikeTools("bash"))
            .WithTools(new NoteTools())
            .Build();

        await agent.RunAsync("go");

        string expected = Normalize("""
            # Role
            r

            # Instructions
            - Default instruction.
            - Keep commands short.
            - Commands run in bash.
            - Notes are plain text.
            - Answer briefly.

            # Constraints
            - Default constraint.
            - Never delete files.
            - Stay polite.
            """);
        Assert.Equal(expected, Normalize(client.Requests[0].Instructions));
    }

    [Fact]
    public async Task Build_SendsTextThatRepeatsOnlyOnce()
    {
        RecordingChatClient client = new(_ => "reply");
        AIAgent agent = client.CreateAgent()
            .WithoutDefaults()
            .WithRole("r")
            .WithConstraint("Never delete files.")
            .WithTools(new ShellLikeTools("bash"))
            .Build();

        await agent.RunAsync("go");

        string prompt = client.Requests[0].Instructions ?? "";
        Assert.Equal(prompt.IndexOf("Never delete files.", StringComparison.Ordinal), prompt.LastIndexOf("Never delete files.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithSystemPrompt_SendsNoCollectionTextOrDefaults()
    {
        RecordingChatClient client = new(_ => "reply");
        AIAgent agent = client.CreateAgent().WithSystemPrompt("x").WithTools(new ShellLikeTools("bash")).Build();

        await agent.RunAsync("go");

        Assert.Equal("x", client.Requests[0].Instructions);
    }

    [Fact]
    public async Task ACollectionToolRunsThroughMafsToolLoop_AndEachToolAndLineIsSentOnce()
    {
        Counter counter = new();
        ScriptedChatClient client = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Increment", new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = client.CreateAgent().WithoutDefaults().WithRole("r").WithTools(counter).Build();

        AgentResponse response = await agent.RunAsync("go");

        Assert.Equal("done", response.Text);
        Assert.Equal(1, counter.Count);
        Assert.All(client.Options, options => Assert.Equal(["Increment"], options!.Tools!.Select(tool => tool.Name)));
        string prompt = client.Options[0]!.Instructions!;
        Assert.Equal(prompt.IndexOf("Counts things.", StringComparison.Ordinal), prompt.LastIndexOf("Counts things.", StringComparison.Ordinal));
        Assert.Equal(prompt, client.Options[1]!.Instructions);
        Assert.Equal($"{Framing}\n\n## Counter\n0", client.Requests[0][^1].Text);
        Assert.Equal($"{Framing}\n\n## Counter\n1", client.Requests[1][^1].Text);
    }

    [Fact]
    public async Task ABuiltAgentStreamsWithContextToo()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = client.CreateAgent().WithoutDefaults().WithRole("r").WithTools(new Counter()).Build();

        await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync("go")) { }

        Assert.Equal($"{Framing}\n\n## Counter\n0", client.Requests[0][^1].Text);
    }

    [Fact]
    public async Task ACloneKeepsTheCollectionsContext()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = client.CreateAgent().WithoutDefaults().WithRole("r").WithTools(new Counter()).Clone().Build();

        await agent.RunAsync("go");

        Assert.Equal($"{Framing}\n\n## Counter\n0", client.Requests[0][^1].Text);
    }

    [Fact]
    public async Task AFailingCollectionContextIsLoggedThroughTheLoggerFactory()
    {
        RecordingLoggerFactory loggers = new();
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = client.CreateAgent().WithoutDefaults().WithRole("r").WithLoggerFactory(loggers).WithTools(new FailingContext()).Build();

        AgentResponse response = await agent.RunAsync("go");

        Assert.Equal("done", response.Text);
        Assert.Single(loggers.Logger.Errors);
    }

    [Fact]
    public async Task AFailingCollectionContextIsLoggedThroughTheLoggerFactoryOfTheServices_WhenNoneWasNamed()
    {
        RecordingLoggerFactory loggers = new();
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        AIAgent agent = client.CreateAgent().WithoutDefaults().WithRole("r").WithServices(new OneService(loggers)).WithTools(new FailingContext()).Build();

        await agent.RunAsync("go");

        Assert.Single(loggers.Logger.Errors);
    }

    private sealed class OneService(ILoggerFactory loggers) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ILoggerFactory) ? loggers : null;
    }

    [Fact]
    public void Build_WithoutRoleOrSystemPromptThrowsNamingBothFixes()
    {
        AgentBuilder builder = new RecordingChatClient(_ => "reply").CreateAgent().WithObjective("o");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("WithRole", error.Message);
        Assert.Contains("WithSystemPrompt", error.Message);
    }

    [Fact]
    public void Build_WithSystemPromptAndASectionThrowsNamingBoth()
    {
        AgentBuilder builder = new RecordingChatClient(_ => "reply").CreateAgent().WithSystemPrompt("x").WithConstraint("c");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("WithSystemPrompt", error.Message);
        Assert.Contains("WithRole", error.Message);
    }

    [Fact]
    public void WithMethods_RejectBlankArgumentsNamingTheArgument()
    {
        AgentBuilder builder = new RecordingChatClient(_ => "reply").CreateAgent();

        Assert.Equal("role", Assert.Throws<ArgumentException>(() => builder.WithRole(" ")).ParamName);
        Assert.Equal("instructions", Assert.Throws<ArgumentException>(() => builder.WithInstructions(["ok", ""])).ParamName);
        Assert.Equal("output", Assert.Throws<ArgumentException>(() => builder.WithExample("in", " ")).ParamName);
        Assert.Equal("toolNames", Assert.Throws<ArgumentException>(() => builder.RequireApproval("RunShell", "")).ParamName);
    }

    [Fact]
    public async Task WithInstructions_ARejectedListAddsNothing()
    {
        RecordingChatClient client = new(_ => "reply");
        AgentBuilder builder = client.CreateAgent().WithoutDefaults().WithRole("r");

        Assert.Throws<ArgumentException>(() => builder.WithInstructions(["kept?", " "]));
        await builder.Build().RunAsync("go");

        Assert.DoesNotContain("kept?", client.Requests[0].Instructions);
    }

    [Fact]
    public void Build_WithTwoToolsOfTheSameNameThrowsNamingBothSources()
    {
        AgentBuilder builder = new RecordingChatClient(_ => "reply").CreateAgent()
            .WithRole("r")
            .WithTool(() => "x", "increment")
            .WithTools(new Counter());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("WithTool", error.Message);
        Assert.Contains($"collection '{nameof(Counter)}'", error.Message);
        Assert.Contains("rename one", error.Message);
    }

    [Fact]
    public void Build_WithBlankCollectionAttributeTextThrowsNamingTheCollection()
    {
        AgentBuilder builder = new RecordingChatClient(_ => "reply").CreateAgent().WithRole("r").WithTools(new BlankTools());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains(nameof(BlankTools), error.Message);
    }

    [Fact]
    public async Task WithTool_AMarkedMethodIsNamedDescribedAndApprovalMarkedLikeACollectionMethod()
    {
        IList<AITool>? tools = null;
        new RecordingChatClient(_ => "reply").CreateAgent().WithoutDefaults().WithRole("r").WithTool(Clock.Now).WithTool(Clock.Reset)
            .ConfigureChatOptions(options => tools = options.Tools).Build();

        Assert.Equal(["now", "Reset"], tools!.Select(tool => tool.Name));
        Assert.Equal("Returns the time.", tools![0].Description);
        Assert.Equal("Resets the clock.", tools[1].Description);
        Assert.IsType<ApprovalRequiredAIFunction>(tools[1]);
    }

    [Fact]
    public void WithTool_AMethodWithoutToolAttributeThrowsNamingTheOtherOverload()
    {
        AgentBuilder builder = new RecordingChatClient(_ => "reply").CreateAgent();

        ArgumentException error = Assert.Throws<ArgumentException>(() => builder.WithTool(Clock.Unmarked));

        Assert.Contains("WithTool(method, name, description)", error.Message);
    }

    [Fact]
    public void WithTool_ALambdaWithANameIsAddedWithThatNameAndDescription()
    {
        IList<AITool>? tools = null;
        new RecordingChatClient(_ => "reply").CreateAgent().WithRole("r").WithTool(() => "pong", "ping", "Answers pong.")
            .ConfigureChatOptions(options => tools = options.Tools).Build();

        AITool tool = Assert.Single(tools!);
        Assert.Equal("ping", tool.Name);
        Assert.Equal("Answers pong.", tool.Description);
    }

    [Fact]
    public void WithTools_KeepsNoReferenceToTheCallersList()
    {
        List<AITool> tools = [AIFunctionFactory.Create(() => "a", "a")];
        IList<AITool>? built = null;
        AgentBuilder builder = new RecordingChatClient(_ => "reply").CreateAgent().WithRole("r").WithTools(tools)
            .ConfigureChatOptions(options => built = options.Tools);

        tools.Add(AIFunctionFactory.Create(() => "b", "b"));
        builder.Build();

        Assert.Equal(["a"], built!.Select(tool => tool.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequireApproval_MakesACollectionToolWaitForTheHost(bool withOwnToolLoop)
    {
        Counter counter = new();
        ScriptedChatClient client = new(
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Increment", new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Assistant, "done"));
        AgentBuilder builder = client.CreateAgent().WithoutDefaults().WithRole("r").WithTools(counter).RequireApproval("increment");
        if (withOwnToolLoop)
        {
            builder.ConfigureToolLoop(loop => loop.MaximumIterationsPerRequest = 3);
        }
        AIAgent agent = builder.Build();
        AgentSession session = await agent.CreateSessionAsync();

        AgentResponse asked = await agent.RunAsync("go", session);
        ToolApprovalRequestContent request = Assert.Single(asked.Messages.SelectMany(message => message.Contents).OfType<ToolApprovalRequestContent>());
        Assert.Equal(0, counter.Count);

        AgentResponse approved = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);

        Assert.Equal(1, counter.Count);
        Assert.Equal("done", approved.Text);
    }

    [Fact]
    public void RequireApproval_NamingAnUnknownToolMakesBuildThrowListingTheTools()
    {
        AgentBuilder builder = new RecordingChatClient(_ => "reply").CreateAgent().WithRole("r").WithTools(new Counter()).RequireApproval("RunShell");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("'RunShell'", error.Message);
        Assert.Contains("'Increment'", error.Message);
    }

    [Fact]
    public void ConfigureToolLoop_SuppliesTheAgentsOnlyToolLoop()
    {
        AIAgent agent = new RecordingChatClient(_ => "reply").CreateAgent().WithRole("r").ConfigureToolLoop(loop => loop.MaximumIterationsPerRequest = 5).Build();

        FunctionInvokingChatClient loop = Assert.Single(Chain(Assert.IsType<ChatClientAgent>(agent.GetService<ChatClientAgent>()).ChatClient).OfType<FunctionInvokingChatClient>());
        Assert.Equal(5, loop.MaximumIterationsPerRequest);
    }

    [Fact]
    public async Task MafHooks_ReachTheBuiltAgent()
    {
        ScriptedChatClient client = new(new ChatMessage(ChatRole.Assistant, "done"));
        InMemoryChatHistoryProvider history = new();
        ExtraInstructionProvider extra = new();
        List<string> seenByMiddleware = [];
        AIAgent agent = client.CreateAgent()
            .WithoutDefaults()
            .WithRole("r")
            .WithName("builder-name")
            .WithTools(new Counter())
            .WithChatHistoryProvider(history)
            .WithContextProvider(extra)
            .ConfigureChatOptions(options => options.Temperature = 0.25f)
            .ConfigureClient(pipeline => pipeline.Use((messages, options, next, cancellationToken) =>
            {
                seenByMiddleware.Add(messages.Last().Text);
                return next(messages, options, cancellationToken);
            }))
            .ConfigureAgentOptions(options => options.Name = "options-name")
            .Build();

        await agent.RunAsync("go");

        ChatClientAgent built = Assert.IsType<ChatClientAgent>(agent.GetService<ChatClientAgent>());
        Assert.Same(history, built.ChatHistoryProvider);
        Assert.Contains(extra, built.AIContextProviders!);
        Assert.Equal("options-name", built.Name);
        Assert.Equal(0.25f, client.Options[0]!.Temperature);
        Assert.EndsWith(ExtraInstructionProvider.Text, client.Options[0]!.Instructions);
        // Middleware sits between the collections' context and the model client, so it sees the context message.
        Assert.Equal([$"{Framing}\n\n## Counter\n0"], seenByMiddleware);
    }

    [Fact]
    public void ConfigureChatOptions_SeesWhatTheBuilderAlreadySet()
    {
        string? seen = null;
        new RecordingChatClient(_ => "reply").CreateAgent().WithoutDefaults().WithRole("r").ConfigureChatOptions(options => seen = options.Instructions).Build();

        Assert.Equal("# Role\nr", Normalize(seen));
    }

    [Fact]
    public async Task Build_TwiceReturnsIndependentAgents()
    {
        RecordingChatClient client = new(_ => "reply");
        List<ChatOptions> options = [];
        AgentBuilder builder = client.CreateAgent().WithoutDefaults().WithRole("r").ConfigureChatOptions(options.Add);
        AIAgent first = builder.Build();
        builder.WithInstruction("Added later.");
        AIAgent second = builder.Build();

        await first.RunAsync("go");
        await second.RunAsync("go");

        Assert.NotSame(first, second);
        Assert.NotSame(options[0], options[1]);
        Assert.DoesNotContain("Added later.", client.Requests[0].Instructions);
        Assert.Contains("Added later.", client.Requests[1].Instructions);
    }

    [Fact]
    public async Task Clone_ChangesToEitherLeaveTheOtherUnchanged()
    {
        RecordingChatClient client = new(_ => "reply");
        AgentBuilder source = client.CreateAgent().WithoutDefaults().WithRole("r").WithInstruction("Shared.");
        AgentBuilder clone = source.Clone().WithInstruction("Clone only.");
        source.WithInstruction("Source only.");

        await source.Build().RunAsync("go");
        await clone.Build().RunAsync("go");

        Assert.Contains("Source only.", client.Requests[0].Instructions);
        Assert.DoesNotContain("Clone only.", client.Requests[0].Instructions);
        Assert.Contains("Clone only.", client.Requests[1].Instructions);
        Assert.DoesNotContain("Source only.", client.Requests[1].Instructions);
    }

    [Fact]
    public void WithId_NameAndDescription_ReachTheAgent()
    {
        AIAgent agent = new RecordingChatClient(_ => "reply").CreateAgent().WithRole("r").WithId("id-1").WithName("n").WithDescription("d").Build();

        Assert.Equal("id-1", agent.Id);
        Assert.Equal("n", agent.Name);
        Assert.Equal("d", agent.Description);
    }

    [Fact]
    public void Apply_RunsSharedSetupOnTheBuilder()
    {
        static void HouseRules(AgentBuilder builder) => builder.WithoutDefaults().WithConstraint("House rule.");

        AIAgent agent = new RecordingChatClient(_ => "reply").CreateAgent().WithRole("r").Apply(HouseRules).Build();

        Assert.Contains("House rule.", Assert.IsType<ChatClientAgent>(agent.GetService<ChatClientAgent>()).Instructions);
    }

    [Fact]
    public async Task BuildOptions_GivesAHandBuiltAgentTheSameInstructionsAndTools_WithContextPerRun()
    {
        ScriptedChatClient built = new(new ChatMessage(ChatRole.Assistant, "done"));
        ScriptedChatClient handBuilt = new(new ChatMessage(ChatRole.Assistant, "done"));
        AgentBuilder builder = built.CreateAgent().WithoutDefaults().WithRole("r").WithTools(new Counter());

        await builder.Build().RunAsync("go");
        await new ChatClientAgent(handBuilt, builder.BuildOptions()).RunAsync("go");

        string prompt = built.Options[0]!.Instructions!;
        Assert.Equal(built.Options[0]!.Tools!.Select(tool => tool.Name), handBuilt.Options[0]!.Tools!.Select(tool => tool.Name));
        Assert.Equal($"{Normalize(prompt)}\n{Framing}\n\n## Counter\n0", Normalize(handBuilt.Options[0]!.Instructions));
        Assert.Single(handBuilt.Requests[0]);
    }

    [Fact]
    public void BuildOptions_AfterConfigureClientThrowsNamingTheFix()
    {
        AgentBuilder builder = new RecordingChatClient(_ => "reply").CreateAgent().WithRole("r").ConfigureClient(_ => { });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.BuildOptions());

        Assert.Contains("ConfigureClient", error.Message);
        Assert.Contains("Build()", error.Message);
    }

    [Fact]
    public void CreateAgent_RejectsANullClient()
    {
        Assert.Equal("chatClient", Assert.Throws<ArgumentNullException>(() => ((IChatClient)null!).CreateAgent()).ParamName);
    }

    private static string Normalize(string? text) => (text ?? "").ReplaceLineEndings("\n");

    private static List<IChatClient> Chain(IChatClient client)
    {
        PropertyInfo inner = typeof(DelegatingChatClient).GetProperty("InnerClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        List<IChatClient> chain = [client];
        while (chain[^1] is DelegatingChatClient delegating)
        {
            chain.Add((IChatClient)inner.GetValue(delegating)!);
        }
        return chain;
    }

    [ToolCollectionInstruction("Keep commands short.")]
    [ToolCollectionConstraint("Never delete files.")]
    private sealed class ShellLikeTools : ToolCollection
    {
        public ShellLikeTools(string shell)
        {
            AddInstruction($"Commands run in {shell}.");
        }

        [Tool("Runs a command. It returns the output. It never prompts.")]
        public string Run(string command) => command;
    }

    [ToolCollectionInstruction("Notes are plain text.")]
    private sealed class NoteTools : ToolCollection
    {
        [Tool("Reads the note. It returns plain text. It changes nothing.")]
        public string ReadNote() => "note";
    }

    [ToolCollectionInstruction("ok", " ")]
    private sealed class BlankTools : ToolCollection;

    [ToolCollectionInstruction("Counts things.")]
    private sealed class Counter : ToolCollection
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        [Tool("Increment", "Adds one to the counter.")]
        public string Increment() => $"{Interlocked.Increment(ref count)}";

        public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>($"## Counter\n{Count}");
    }

    private sealed class FailingContext : ToolCollection
    {
        public override ValueTask<string?> GetContextAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("context broke");
    }

    private static class Clock
    {
        [Tool("now", "Returns the time.")]
        public static string Now() => "noon";

        [Tool(RequiresApproval = true)]
        [Description("Resets the clock.")]
        public static string Reset() => "reset";

        public static string Unmarked() => "unmarked";
    }

    private sealed class ExtraInstructionProvider : AIContextProvider
    {
        public const string Text = "Extra from a provider.";

        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default) =>
            new(new AIContext { Instructions = Text });
    }
}
