using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.SubAgents;
using PinkRooster.ToolCollections;

namespace PinkRooster.Agents.Tests.Permissions;

public sealed class PermissionPolicyTests
{
    // One tool of each kind, every one needing approval, so every call reaches the policy.
    private sealed class Workbench : ToolCollection
    {
        private readonly List<string> ran = [];

        public IReadOnlyList<string> Ran
        {
            get
            {
                lock (ran)
                {
                    return [.. ran];
                }
            }
        }

        [Tool("ReadFile", "Reads a file.", RequiresApproval = true, Kind = ToolKind.Read)]
        public string ReadFile(string path) => Did($"ReadFile {path}");

        [Tool("TaskAdd", "Adds a task.", RequiresApproval = true, Kind = ToolKind.State)]
        public string TaskAdd(string title) => Did($"TaskAdd {title}");

        [Tool("EditFile", "Edits a file.", RequiresApproval = true, Kind = ToolKind.Edit)]
        public string EditFile(string path) => Did($"EditFile {path}");

        [Tool("RunShell", "Runs a command.", RequiresApproval = true, Kind = ToolKind.Execute)]
        public string RunShell(string command) => Did($"RunShell {command}");

        [Tool("Deploy", "Declares no kind.", RequiresApproval = true)]
        public string Deploy(string target) => Did($"Deploy {target}");

        private string Did(string what)
        {
            lock (ran)
            {
                ran.Add(what);
            }
            return $"did {what}";
        }
    }

    // The user: records what was asked and answers from a queue, Allow when the queue is empty.
    private sealed class User
    {
        public List<ApprovalQuestion> Asked { get; } = [];

        public Queue<ApprovalChoice> Answers { get; } = [];

        public Task<ApprovalChoice> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
        {
            Asked.Add(question);
            return Task.FromResult(Answers.TryDequeue(out ApprovalChoice answer) ? answer : ApprovalChoice.Allow);
        }
    }

    private static FunctionCallContent Call(string name, string argument, string value) => new($"call-{Guid.NewGuid():N}", name, new Dictionary<string, object?> { [argument] = value });

    private static FunctionCallContent Shell(string command) => Call("RunShell", "command", command);

    private static ChatMessage Says(FunctionCallContent call) => new(ChatRole.Assistant, [call]);

    private static PermissionPolicy Policy(User user, Workbench tools, PermissionMode mode = PermissionMode.Ask)
    {
        PermissionPolicy policy = new PermissionPolicy(user.AskAsync).WithTools(tools).WithCommandArgument("RunShell", "command");
        policy.Mode = mode;
        return policy;
    }

    // Runs an agent whose model makes the calls one after another, answered by the policy, and returns what each call answered.
    private static async Task<IReadOnlyList<string>> RunAsync(PermissionPolicy policy, Workbench tools, params FunctionCallContent[] calls)
    {
        ChatMessage[] script = [.. calls.Select(Says), new ChatMessage(ChatRole.Assistant, "done")];
        ScriptedChatClient model = new(script);
        AIAgent agent = model.CreateAgent().WithoutDefaults().WithRole("r").WithTools(tools).Build();

        AgentResponse response = await agent.RunWithApprovalsAsync("go", session: null, policy.AnswerAsync, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("done", response.Text);
        return [.. calls.Select(call => model.Requests.SelectMany(request => request).SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>().First(result => result.CallId == call.CallId).Result?.ToString() ?? string.Empty)];
    }

    [Fact]
    public async Task AnEditTool_AsksInAsk_RunsUnaskedInAcceptEdits_AndIsRefusedInPlanWithAnAnswerThatNamesTheMode()
    {
        Workbench tools = new();
        User user = new();

        await RunAsync(Policy(user, tools, PermissionMode.Ask), tools, Call("EditFile", "path", "a.cs"));
        Assert.Equal(["EditFile"], user.Asked.Select(question => question.Call.Name));
        Assert.Equal(["EditFile a.cs"], tools.Ran);

        await RunAsync(Policy(user, tools, PermissionMode.AcceptEdits), tools, Call("EditFile", "path", "b.cs"));
        Assert.Single(user.Asked);
        Assert.Equal(["EditFile a.cs", "EditFile b.cs"], tools.Ran);

        IReadOnlyList<string> answers = await RunAsync(Policy(user, tools, PermissionMode.Plan), tools, Call("EditFile", "path", "c.cs"));
        Assert.Single(user.Asked);
        Assert.Equal(2, tools.Ran.Count);
        Assert.Contains("permission mode is 'plan'", Assert.Single(answers));
        Assert.Contains("EditFile", answers[0]);
    }

    [Theory]
    [InlineData(PermissionMode.Ask)]
    [InlineData(PermissionMode.AcceptEdits)]
    [InlineData(PermissionMode.Plan)]
    public async Task AReadOrStateToolThatNeedsApproval_RunsUnaskedInEveryMode(PermissionMode mode)
    {
        Workbench tools = new();
        User user = new();

        await RunAsync(Policy(user, tools, mode), tools, Call("ReadFile", "path", "a.cs"), Call("TaskAdd", "title", "Fix it"));

        Assert.Empty(user.Asked);
        Assert.Equal(["ReadFile a.cs", "TaskAdd Fix it"], tools.Ran);
    }

    [Theory]
    [InlineData(PermissionMode.Ask, true)]
    [InlineData(PermissionMode.AcceptEdits, true)]
    [InlineData(PermissionMode.Plan, false)]
    public async Task NoModeAllowsAToolWithNoKind_AndARuleThatNamesItDoes(PermissionMode mode, bool asks)
    {
        Workbench tools = new();
        User user = new();
        user.Answers.Enqueue(ApprovalChoice.Skip);

        IReadOnlyList<string> answers = await RunAsync(Policy(user, tools, mode), tools, Call("Deploy", "target", "prod"));

        Assert.Equal(asks ? ["Deploy"] : [], user.Asked.Select(question => question.Call.Name));
        Assert.Empty(tools.Ran);
        Assert.Contains(asks ? "did not allow" : "plan", answers[0]);

        await RunAsync(Policy(user, tools, mode).Allow(AllowRule.ForTool("Deploy")), tools, Call("Deploy", "target", "prod"));

        Assert.Equal(asks ? 1 : 0, user.Asked.Count);
        Assert.Equal(["Deploy prod"], tools.Ran);
    }

    [Fact]
    public async Task AToolThePolicyWasNotTold_HasNoKind_AndSoHasANameTwoToolsOfDifferentKindsShare()
    {
        User user = new();
        AIFunction read = new ExternalToolCollection("A", [AIFunctionFactory.Create(() => "x", "Search")]).WithKind(ToolKind.Read, "Search").GetAIFunctions()[0];
        AIFunction edit = new ExternalToolCollection("B", [AIFunctionFactory.Create(() => "x", "Search")]).WithKind(ToolKind.Edit, "Search").GetAIFunctions()[0];
        PermissionPolicy policy = new PermissionPolicy(user.AskAsync).WithTools([read]);

        Assert.True((await policy.AnswerAsync(Call("Search", "q", "x"), TestContext.Current.CancellationToken)).Approved);
        Assert.Empty(user.Asked);

        await policy.AnswerAsync(Call("Unknown", "q", "x"), TestContext.Current.CancellationToken);
        policy.WithTools([edit]);
        await policy.AnswerAsync(Call("Search", "q", "x"), TestContext.Current.CancellationToken);

        Assert.Equal(["Unknown", "Search"], user.Asked.Select(question => question.Call.Name));
    }

    [Fact]
    public async Task ASessionWideAnswerToACommand_KeepsItsPrefix_AndNothingBeyondIt()
    {
        Workbench tools = new();
        User user = new();
        PermissionPolicy policy = Policy(user, tools);
        user.Answers.Enqueue(ApprovalChoice.AllowForSession);
        CancellationToken none = TestContext.Current.CancellationToken;

        Assert.True((await policy.AnswerAsync(Shell("dotnet build -c Release"), none)).Approved);

        AllowRule kept = Assert.Single(policy.SessionRules);
        Assert.Equal(("RunShell", "command", "dotnet build"), (kept.Tool, kept.Argument, kept.Prefix));
        Assert.Same(kept, Assert.Single(user.Asked).SessionRule);
        Assert.Empty(policy.Rules);

        // A second build runs unasked.
        Assert.True((await policy.AnswerAsync(Shell("dotnet build"), none)).Approved);
        Assert.Single(user.Asked);

        // Another subcommand, another program and a chained command are asked about; the chained one is skipped here.
        user.Answers.Enqueue(ApprovalChoice.Allow);
        user.Answers.Enqueue(ApprovalChoice.Allow);
        user.Answers.Enqueue(ApprovalChoice.Skip);
        Assert.True((await policy.AnswerAsync(Shell("dotnet test"), none)).Approved);
        Assert.True((await policy.AnswerAsync(Shell("git status"), none)).Approved);
        Assert.False((await policy.AnswerAsync(Shell("dotnet build; rm -rf ."), none)).Approved);
        Assert.Equal(["dotnet build -c Release", "dotnet test", "git status", "dotnet build; rm -rf ."],
            user.Asked.Select(question => question.Call.Arguments!["command"]!.ToString()));

        // The answer was given at a prompt, so plan mode does not honour it.
        policy.Mode = PermissionMode.Plan;
        ApprovalAnswer inPlan = await policy.AnswerAsync(Shell("dotnet build"), none);
        Assert.False(inPlan.Approved);
        Assert.Contains("plan", inPlan.Reason);
        Assert.Equal(4, user.Asked.Count);
    }

    [Fact]
    public async Task AfterReset_TheSameCallAsksAgain_TheModeIsAsk_AndConfiguredRulesStay()
    {
        Workbench tools = new();
        User user = new();
        PermissionPolicy policy = Policy(user, tools, PermissionMode.AcceptEdits).Allow(AllowRule.ForPrefix("RunShell", "command", "git status"));
        user.Answers.Enqueue(ApprovalChoice.AllowForSession);
        CancellationToken none = TestContext.Current.CancellationToken;
        await policy.AnswerAsync(Shell("dotnet build"), none);
        await policy.AnswerAsync(Shell("dotnet build"), none);
        Assert.Single(user.Asked);

        policy.Reset();

        Assert.Equal(PermissionMode.Ask, policy.Mode);
        Assert.Empty(policy.SessionRules);
        await policy.AnswerAsync(Shell("dotnet build"), none);
        await policy.AnswerAsync(Shell("git status --short"), none);
        Assert.Equal(2, user.Asked.Count);
    }

    [Fact]
    public async Task InPlan_AConfiguredRuleLetsACommandRun_AndNeverAnEdit()
    {
        Workbench tools = new();
        User user = new();
        PermissionPolicy policy = Policy(user, tools, PermissionMode.Plan)
            .Allow(AllowRule.ForPrefix("RunShell", "command", "git log"), AllowRule.ForTool("EditFile"));
        CancellationToken none = TestContext.Current.CancellationToken;

        Assert.True((await policy.AnswerAsync(Shell("git log -5"), none)).Approved);
        Assert.False((await policy.AnswerAsync(Shell("git push"), none)).Approved);
        Assert.False((await policy.AnswerAsync(Shell("git log; git push"), none)).Approved);
        Assert.False((await policy.AnswerAsync(Call("EditFile", "path", "a.cs"), none)).Approved);
        Assert.Empty(user.Asked);

        // Out of plan mode the same rule on the edit tool counts.
        policy.Mode = PermissionMode.Ask;
        Assert.True((await policy.AnswerAsync(Call("EditFile", "path", "a.cs"), none)).Approved);
        Assert.Empty(user.Asked);
    }

    [Fact]
    public async Task TheSessionWideOffer_IsTheWholeTool_OrTheCommandsStart_OrNothingForAChainedCommand()
    {
        Workbench tools = new();
        User user = new();
        PermissionPolicy policy = Policy(user, tools);
        CancellationToken none = TestContext.Current.CancellationToken;

        await policy.AnswerAsync(Call("EditFile", "path", "a.cs"), none);
        await policy.AnswerAsync(Shell("ls -la src"), none);
        await policy.AnswerAsync(Shell("git status --short"), none);
        user.Answers.Enqueue(ApprovalChoice.AllowForSession);
        await policy.AnswerAsync(Shell("make all && make install"), none);

        Assert.Equal(["every EditFile call", "RunShell starting with \"ls\"", "RunShell starting with \"git status\"", null], user.Asked.Select(question => question.SessionRule?.ToString()));
        // A session-wide answer with nothing on offer allows that one call and keeps no rule.
        Assert.Empty(policy.SessionRules);
        await policy.AnswerAsync(Shell("make all && make install"), none);
        Assert.Equal(5, user.Asked.Count);
    }

    [Fact]
    public async Task TheUserIsAskedOneQuestionAtATime_AndACallCoveredByTheAnswerBeforeIt_IsNotAsked()
    {
        Workbench tools = new();
        TaskCompletionSource<ApprovalChoice> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int asked = 0;
        PermissionPolicy policy = new PermissionPolicy((_, _) => Interlocked.Increment(ref asked) == 1 ? first.Task : Task.FromResult(ApprovalChoice.Skip))
            .WithTools(tools).WithCommandArgument("RunShell", "command");
        CancellationToken none = TestContext.Current.CancellationToken;

        Task<ApprovalAnswer> one = policy.AnswerAsync(Shell("dotnet build"), none);
        Task<ApprovalAnswer> two = policy.AnswerAsync(Shell("dotnet build -c Release"), none);
        Task<ApprovalAnswer> other = policy.AnswerAsync(Shell("git push"), none);
        await Task.Delay(50, none);
        Assert.Equal(1, Volatile.Read(ref asked));
        Assert.False(two.IsCompleted);

        first.SetResult(ApprovalChoice.AllowForSession);

        Assert.True((await one).Approved);
        Assert.True((await two).Approved);
        Assert.False((await other).Approved);
        Assert.Equal(2, Volatile.Read(ref asked));
    }

    [Fact]
    public async Task ASubAgentsCalls_AreAnsweredByTheSamePolicy()
    {
        Workbench tools = new();
        User user = new();
        PermissionPolicy policy = Policy(user, tools);
        FunctionCallContent edit = Call("EditFile", "path", "a.cs");
        FunctionCallContent again = Call("EditFile", "path", "b.cs");
        FunctionCallContent planned = Call("EditFile", "path", "c.cs");
        ScriptedChatClient helper = new(Says(edit), Says(again), new ChatMessage(ChatRole.Assistant, "edited"), Says(planned), new ChatMessage(ChatRole.Assistant, "planned"));
        SubAgentToolCollection subAgents = new SubAgentToolCollectionBuilder()
            .WithModel("fast", "For tests.", helper)
            .WithToolSet("files", "Changing files.", tools)
            .ApproveToolCallsWith(policy.AnswerAsync)
            .Build();
        user.Answers.Enqueue(ApprovalChoice.AllowForSession);

        // The user's session-wide answer to the helper's first edit covers its second.
        Assert.Equal("edited", await subAgents.RunSubAgent("fast", "Fixer", "r", "p", toolSets: ["files"], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(user.Asked);
        Assert.Equal(["EditFile a.cs", "EditFile b.cs"], tools.Ran);

        // The mode the host set counts for the helper too, and the helper reads why.
        policy.Mode = PermissionMode.Plan;
        Assert.Equal("planned", await subAgents.RunSubAgent("fast", "Fixer", "r", "p", toolSets: ["files"], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, tools.Ran.Count);
        Assert.Contains(helper.Requests.SelectMany(request => request).SelectMany(message => message.Contents).OfType<FunctionResultContent>(),
            result => result.CallId == planned.CallId && (result.Result?.ToString() ?? "").Contains("permission mode is 'plan'"));
    }

    [Fact]
    public void BlankAndNullValues_Throw()
    {
        PermissionPolicy policy = new((_, _) => Task.FromResult(ApprovalChoice.Allow));

        Assert.Throws<ArgumentNullException>(() => new PermissionPolicy(null!));
        Assert.Throws<ArgumentException>(() => policy.WithCommandArgument(" ", "command"));
        Assert.Throws<ArgumentException>(() => policy.WithCommandArgument("RunShell", ""));
        Assert.Throws<ArgumentException>(() => policy.Allow(null!, AllowRule.ForTool("x")));
        Assert.Throws<ArgumentNullException>(() => policy.WithTools((IEnumerable<AITool>)null!));
    }
}
