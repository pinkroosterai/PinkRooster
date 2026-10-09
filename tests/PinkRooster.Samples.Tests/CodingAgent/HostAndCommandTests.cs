using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Extensions.AI;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.SpectreConsole;
using PinkRooster.SpectreConsole.Tests.TestSupport;
using static PinkRooster.Samples.Tests.CodingAgent.Showcase;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>The program around the assistant: how it starts and ends, the project files it reads, and its commands.</summary>
public sealed class HostAndCommandTests
{
    private static ChatMessage AddTask(string subject) =>
        Call("TaskAdd", ("tasks", new[] { new Dictionary<string, object?> { ["subject"] = subject } }));

    [Fact]
    public async Task InAnEmptyDirectory_ItStarts_AnswersOnePrompt_AndExitsWithZero()
    {
        using Showcase showcase = new();
        ScriptedChatClient model = new(Text("Hello, what shall we build?"));

        int exit = await showcase.RunAsync(model, "hello");

        Assert.Equal(0, exit);
        Assert.Single(model.Requests);
        Assert.Contains("Coding assistant on test-model", showcase.Console.Output);
        // Not a git repository, no instruction file, no config file: the assistant works without them.
        Assert.Contains(model.Requests[0], message => message.Text.Contains("Permission mode: ask.") && !message.Text.Contains("Git:"));
        string[] tools = [.. model.Options[0]!.Tools!.Select(tool => tool.Name)];
        Assert.Superset(
            new HashSet<string> { "ReadFile", "EditFile", "CreateFile", "WriteFile", "MoveFile", "DeleteFile", "RunShell", "AskUserQuestion", "TaskAdd", "GetDaysBetween", "RunSubAgent", "WaitForTasks", "CancelTask" },
            new HashSet<string>(tools));
        // The model called no tool, and the program says so once when it ends.
        Assert.Contains("The model called no tool in this session.", showcase.Console.Output);
    }

    [Fact]
    public async Task TheSystemPrompt_AsksForTheWalkthrough_AndHoldsTheInstructionFile_WhoseSizeIsPrinted()
    {
        using Showcase showcase = new();
        showcase.Write("AGENTS.md", "Build with `make all`.\n");
        ScriptedChatClient model = new(Text("ok"));

        await showcase.RunAsync(model, "hello");

        string system = model.Options[0]!.Instructions!;
        Assert.Contains("# Output Format", system);
        Assert.Contains("end your answer with a walkthrough", system);
        Assert.Contains("Build with `make all`.", system);
        Assert.Contains("Read AGENTS.md: 23 characters", showcase.Console.Output);
    }

    [Fact]
    public async Task InAGitRepository_TheModelReadsTheBranchAndTheCountOfChangedFiles()
    {
        using Showcase showcase = new();
        if (!await TryGitInitAsync(showcase.Workspace))
        {
            Assert.Skip("git is not installed on this machine.");
        }
        showcase.Write("new.txt", "untracked\n");
        ScriptedChatClient model = new(Text("ok"));

        await showcase.RunAsync(model, "hello", "/diff");

        Assert.Contains(model.Requests[0], message => message.Text.Contains("Git: branch ") && message.Text.Contains(", 1 changed file"));
        Assert.Contains("new file: new.txt", showcase.Console.Output);
    }

    [Fact]
    public async Task PlanMode_RunsTheRequestInPlan_AndCarryingItOut_SwitchesToAsk()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "one\n");
        ScriptedChatClient model = new(Text("Plan: change one to two."), Edit("a.txt", "one", "two"), Text("done"), Text("checked"));

        await showcase.RunAsync(model, "/plan change one to two");

        Assert.Contains(model.Requests[0], message => message.Text.Contains("Permission mode: plan."));
        Assert.Equal("What do you want to do with this plan?", Assert.Single(showcase.Console.QuestionsAsked).Question);
        Assert.Equal(1, model.Requests[1].Count(message => message.Text == "Carry out the plan."));
        Assert.Contains(model.Requests[1], message => message.Text.Contains("Permission mode: ask."));
        Assert.Equal(["EditFile"], showcase.Console.ApprovalsAsked.Select(question => question.Call.Name));
        Assert.Equal("two\n", File.ReadAllText(showcase.PathOf("a.txt")));
    }

    [Fact]
    public async Task PlanMode_Revised_AnswersWithANewPlan_AndLeftAlone_StaysInPlan()
    {
        using Showcase showcase = new();
        ScriptedChatClient model = new(Text("Plan A."), Text("Plan B."));
        showcase.Console.Picks.Enqueue("Revise it");
        showcase.Console.Picks.Enqueue("Leave it");

        await showcase.RunAsync(model, "/plan do the thing", "and keep it small", "/mode");

        Assert.Equal(2, showcase.Console.QuestionsAsked.Count);
        Assert.Equal(1, CountInAll(model, "and keep it small"));
        Assert.Contains(model.Requests[1], message => message.Text.Contains("Permission mode: plan."));
        Assert.Contains("The permission mode is still plan", showcase.Console.Output);
        Assert.Contains("The permission mode is plan.", showcase.Console.Output);
    }

    [Fact]
    public async Task Tasks_ShowsTheConversationsList_AndClear_StartsANewConversation()
    {
        using Showcase showcase = new();
        ScriptedChatClient model = new(AddTask("Write the parser"), Text("noted"), Text("a fresh start"));

        await showcase.RunAsync(model, "/tasks", "plan it", "/tasks", "/clear", "/tasks", "hello again");

        string output = showcase.Console.Output;
        Assert.Equal(2, output.Split("The task list is empty.").Length - 1);
        Assert.Contains("[ ] #1 Write the parser", output);
        Assert.Contains("New conversation.", output);
        // The request after /clear holds nothing of the conversation before it.
        Assert.Equal(["hello again"], model.Requests[^1].Where(message => message.Role == ChatRole.User && !message.Text.StartsWith("The current state")).Select(message => message.Text));
    }

    [Fact]
    public async Task HelpListsTheCommands_AnUnknownCommandListsThemToo_AndExitEndsTheProgram()
    {
        using Showcase showcase = new();
        ScriptedChatClient model = new(Text("never asked"));

        int exit = await showcase.RunAsync(model, "/help", "/nope", "/mode sideways", "/exit", "not read any more");

        Assert.Equal(0, exit);
        Assert.Empty(model.Requests);
        string output = showcase.Console.Output;
        Assert.Contains("Unknown command /nope.", output);
        Assert.Equal(2, output.Split("/plan <request>").Length - 1);
        Assert.Contains("There is no mode 'sideways'.", output);
        Assert.Equal(["not read any more"], showcase.Console.Inputs);
    }

    [Fact]
    public async Task CtrlCAtThePrompt_EndsTheProgramWith130_AndDuringARun_StopsTheRunOnly()
    {
        using Showcase showcase = new();
        // The first model call interrupts, as Ctrl+C during a run does; the prompt then returns and takes the next line.
        Interrupting model = new(showcase.Interrupt, Text("after the interrupt"));
        showcase.Console.Inputs.Enqueue("first");
        showcase.Console.Inputs.Enqueue("second");

        int exit = await showcase.RunAsync(model, showcase.Console);

        Assert.Equal(0, exit);
        Assert.Contains("Stopped. The conversation is as it stands.", showcase.Console.Output);
        Assert.Equal(2, model.Calls);

        using Showcase atPrompt = new();
        atPrompt.Interrupt.Raise();
        Assert.Equal(130, await atPrompt.RunAsync(new ScriptedChatClient(Text("never asked")), "hello"));
    }

    [Fact]
    public async Task WithoutATerminal_OrWithBrokenSettings_ItExitsWithOne_AndSaysWhy()
    {
        using Showcase showcase = new();
        RecordingAgentConsole piped = new() { IsInteractive = false };
        Assert.Equal(1, await showcase.RunAsync(new ScriptedChatClient(Text("x")), piped));
        Assert.Contains("needs an interactive terminal", piped.Output);

        File.WriteAllText(showcase.SettingsPath, "{ \"models\": [ { \"model\": \"m\" } ] }");
        Assert.Equal(1, await showcase.RunAsync(new ScriptedChatClient(Text("x")), "hello"));
        Assert.Contains("needs an \"endpoint\"", showcase.Console.Output);

        using Showcase brokenConfig = new();
        brokenConfig.Config("""{ "allow": [ { "tool": "RunShell", "prefix": "git" } ] }""");
        Assert.Equal(1, await brokenConfig.RunAsync(new ScriptedChatClient(Text("x")), "hello"));
        Assert.Contains("has a \"prefix\" and no \"argument\"", brokenConfig.Console.Output);
    }

    [Fact]
    public async Task TheFirstRun_CreatesTheModelSettings_AndSaysWhereTheyAre()
    {
        using Showcase showcase = new();
        File.Delete(showcase.SettingsPath);

        await showcase.RunAsync(new ScriptedChatClient(Text("hi")), "hello");

        Assert.True(File.Exists(showcase.SettingsPath));
        Assert.Contains($"Created {showcase.SettingsPath}", showcase.Console.Output);
        Assert.Contains("\"contextSize\"", File.ReadAllText(showcase.SettingsPath));
    }

    [Fact]
    public async Task AModelCallThatFails_EndsTheProgramWithOne_NamingTheSettingsFile()
    {
        using Showcase showcase = new();

        int exit = await showcase.RunAsync(new ScriptedChatClient(Array.Empty<ChatMessage>()), "hello");

        Assert.Equal(1, exit);
        Assert.Contains(showcase.SettingsPath, showcase.Console.Output);
    }

    [Fact]
    public async Task ACommandTypedDuringARun_IsHeld_IsInNoRequest_AndRunsAfterTheRun()
    {
        using Showcase showcase = new();
        KeyedConsole terminal = new();
        AgentConsole console = new(terminal);
        Waiting model = new(Text("done"));

        Task<int> program = showcase.RunAsync(model, console);
        terminal.Keys.Submit("go");
        await model.Called.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        // The run is in progress: this line is a command, so it is held and not posted to the model.
        terminal.Keys.Submit("/tasks");
        await WaitUntilAsync(() => !terminal.Keys.IsKeyAvailable());
        model.Release();
        await WaitUntilAsync(() => terminal.Output.Contains("The task list is empty."));
        terminal.Keys.Submit("/exit");
        int exit = await program.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Contains("held until the run ends: /tasks", terminal.Output);
        Assert.DoesNotContain(model.Requests.SelectMany(request => request), message => message.Text.Contains("/tasks"));
    }

    [Fact]
    public void TheProjectFile_HasNoReferenceToTheSharedSampleLibrary()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "PinkRooster.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("The repository root was not found above the test's directory.");
        }
        XDocument project = XDocument.Load(Path.Combine(root, "samples", "PinkRooster.Samples.CodingAgent", "PinkRooster.Samples.CodingAgent.csproj"));

        string[] references = [.. project.Descendants("ProjectReference").Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')))];

        Assert.NotEmpty(references);
        Assert.DoesNotContain("PinkRooster.Samples", references);
        Assert.All(references, reference => Assert.False(reference.StartsWith("PinkRooster.Samples", StringComparison.Ordinal), reference));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource limit = new(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, limit.Token);
        }
    }

    private static async Task<bool> TryGitInitAsync(string directory)
    {
        try
        {
            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("git", "init --quiet") { WorkingDirectory = directory, UseShellExecute = false });
            if (process is null)
            {
                return false;
            }
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    // A model whose first call raises the interrupt and then waits to be cancelled, as a slow model does when Ctrl+C is pressed.
    private sealed class Interrupting(PinkRooster.Samples.CodingAgent.Interrupt interrupt, ChatMessage then) : IChatClient
    {
        public int Calls { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (++Calls == 1)
            {
                interrupt.Raise();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return new ChatResponse(then);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ChatResponseUpdate update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    // A model that waits to be released before it answers, so a test can type while the run is in progress.
    private sealed class Waiting(ChatMessage answer) : IChatClient
    {
        private readonly TaskCompletionSource called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<List<ChatMessage>> Requests { get; } = [];

        public Task Called => called.Task;

        public void Release() => released.TrySetResult();

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            lock (Requests)
            {
                Requests.Add([.. messages]);
            }
            called.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
            return new ChatResponse(answer);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ChatResponseUpdate update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
