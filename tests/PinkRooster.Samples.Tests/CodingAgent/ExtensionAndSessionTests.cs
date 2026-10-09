using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using PinkRooster.Agents.Permissions;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using PinkRooster.ToolCollections.Mcp;
using static PinkRooster.Samples.Tests.CodingAgent.Showcase;
using Sample = PinkRooster.Samples.CodingAgent;

namespace PinkRooster.Samples.Tests.CodingAgent;

/// <summary>Skills and MCP servers, saved conversations, undo, and the status line.</summary>
public sealed class ExtensionAndSessionTests
{
    private static ChatMessage AddTask(string subject) =>
        Call("TaskAdd", ("tasks", new[] { new Dictionary<string, object?> { ["subject"] = subject } }));

    private static string Told(ScriptedChatClient model, int request) =>
        string.Join("\n", [model.Options[request]?.Instructions ?? string.Empty, .. model.Requests[request].Select(message => message.Text)]);

    private static async Task<InProcessMcpServer> NotesServerAsync() => await InProcessMcpServer.StartAsync(
        null,
        McpServerTool.Create((string query) => $"notes about {query}", new() { Name = "search_notes", ReadOnly = true }),
        McpServerTool.Create((string text) => $"saved {text}", new() { Name = "save_note" }));

    [Fact]
    public async Task ASkillOfTheWorkspace_IsOfferedByItsDescription()
    {
        using Showcase showcase = new();
        showcase.Write(".agents/skills/release-notes/SKILL.md",
            "---\nname: release-notes\ndescription: Writes release notes from the commits since the last tag.\n---\n\n# Release notes\n\nList the commits, group them, write the notes.\n");
        ScriptedChatClient model = new(Text("ok"));

        await showcase.RunAsync(model, "hello");

        Assert.Contains("Writes release notes from the commits since the last tag.", Told(model, 0));
        Assert.Contains(model.Options[0]!.Tools!, tool => tool.Name == "load_skill");
        Assert.Contains("Skills: 1.", showcase.Console.Output);
    }

    [Fact]
    public async Task AnMcpServersTools_AreOffered_AndOneNotMarkedReadOnly_AsksInAcceptEdits()
    {
        await using InProcessMcpServer notes = await NotesServerAsync();
        using Showcase showcase = new();
        showcase.McpServers.Add(new Sample.Project.McpServer("Notes", new Uri("http://in-process.invalid/mcp"), null, []));
        showcase.ConnectMcp = (server, token) => McpToolCollection.CreateAsync(server.Name, notes.Client, cancellationToken: token);
        ScriptedChatClient model = new(Call("search_notes", ("query", "tea")), Call("save_note", ("text", "green")), Text("saved"));

        await showcase.RunAsync(model, "/mode accept-edits", "note it", "/mcp");

        Assert.Contains(model.Options[0]!.Tools!, tool => tool.Name == "search_notes");
        // The read-only tool ran unasked. The other has no kind, so accept-edits does not let it through.
        Assert.Equal(["save_note"], showcase.Console.ApprovalsAsked.Select(question => question.Call.Name));
        IReadOnlyList<(string Name, string Result)> results = Results(model);
        Assert.Contains("notes about tea", results[0].Result);
        Assert.Contains("saved green", results[1].Result);
        Assert.Contains("MCP servers: Notes.", showcase.Console.Output);
        Assert.Contains("save_note  (asks first)", showcase.Console.Output);
    }

    [Fact]
    public async Task AServerThatRefusesTheConnection_IsNamedInOneWarning_AndTheProgramAnswers()
    {
        using Showcase showcase = new();
        // Nothing listens on port 1 of this machine, so the connection is refused at once; no network is used.
        showcase.McpServers.Add(new Sample.Project.McpServer("Offline", new Uri("http://127.0.0.1:1/mcp"), null, []));
        ScriptedChatClient model = new(Text("still here"));

        int exit = await showcase.RunAsync(model, "hello", "/mcp");

        Assert.Equal(0, exit);
        Assert.Single(model.Requests);
        string warning = Assert.Single(showcase.Console.Output.Split('\n'), line => line.Contains("Offline"));
        Assert.StartsWith("MCP server 'Offline' could not be reached and is left out:", warning);
        Assert.Contains("No MCP server is connected.", showcase.Console.Output);
    }

    [Fact]
    public async Task TheServersOfTheConfigFile_ReplaceTheDefaultOnes()
    {
        using Showcase showcase = new();
        showcase.McpServers.Add(new Sample.Project.McpServer("Default", new Uri("http://127.0.0.1:1/mcp"), null, []));
        showcase.Config("""{ "mcpServers": [ { "name": "Local", "command": "definitely-not-a-program-on-this-machine", "arguments": ["--stdio"] } ] }""");
        List<string> asked = [];
        showcase.ConnectMcp = (server, _) =>
        {
            asked.Add($"{server.Name}:{server.Command}:{string.Join(",", server.Arguments)}");
            return Task.FromException<McpToolCollection>(new InvalidOperationException("not started"));
        };

        await showcase.RunAsync(new ScriptedChatClient(Text("ok")), "hello");

        Assert.Equal(["Local:definitely-not-a-program-on-this-machine:--stdio"], asked);
        Assert.Contains("MCP server 'Local' could not be reached and is left out: not started", showcase.Console.Output);
    }

    [Fact]
    public async Task Resume_BringsTheConversationAndItsTaskListBack_AndSetsTheModeToAsk()
    {
        using Showcase first = new();
        ScriptedChatClient earlier = new(AddTask("Write the parser"), Text("planned"));
        await first.RunAsync(earlier, "/mode accept-edits", "plan the work");
        Assert.Single(Directory.GetFiles(Path.Combine(first.Workspace, ".pinkrooster", "sessions"), "*.json"));

        using Showcase second = first.Again();
        ScriptedChatClient later = new(Text("going on"));
        await second.RunAsync(later, "/tasks", "/resume", "/tasks", "/mode", "what is next?");

        string output = second.Console.Output;
        Assert.Equal("Which conversation do you want to go on with?", Assert.Single(second.Console.QuestionsAsked).Question);
        Assert.Contains("plan the work", second.Console.QuestionsAsked[0].Options[0].Label);
        Assert.Contains("The task list is empty.", output);
        Assert.Contains("[ ] #1 Write the parser", output);
        Assert.Contains("The permission mode is ask.", output);
        // The next request holds the saved messages followed by the new prompt, and the task list in the tools' context.
        List<ChatMessage> request = Assert.Single(later.Requests);
        Assert.Equal(["plan the work", "", "", "planned", "what is next?"],
            request.Where(message => !message.Text.StartsWith("The current state")).Select(message => message.Text));
        Assert.Contains(request, message => message.Text.Contains("#1 [pending] Write the parser"));
        // It goes on in the file it came from.
        Assert.Single(Directory.GetFiles(Path.Combine(first.Workspace, ".pinkrooster", "sessions"), "*.json"));
    }

    [Fact]
    public async Task Resume_NamesTheToolsThatAreGoneAndNew_AndShowsAnUnreadableFileWithoutOfferingIt()
    {
        await using InProcessMcpServer notes = await NotesServerAsync();
        using Showcase first = new();
        first.McpServers.Add(new Sample.Project.McpServer("Notes", new Uri("http://in-process.invalid/mcp"), null, []));
        first.ConnectMcp = (server, token) => McpToolCollection.CreateAsync(server.Name, notes.Client, cancellationToken: token);
        await first.RunAsync(new ScriptedChatClient(Text("hi")), "hello");
        string broken = first.Write(".pinkrooster/sessions/20260101-000000-broken.json", "{ not json");

        // The next day the server is gone and the workspace has a skill.
        using Showcase second = first.Again();
        second.Write(".agents/skills/review/SKILL.md", "---\nname: review\ndescription: Reviews a change.\n---\n\nRead the diff.\n");
        await second.RunAsync(new ScriptedChatClient(Text("going on")), "/resume", "go on");

        string output = second.Console.Output;
        Assert.Contains("Tools it was saved with that are gone: search_notes, save_note.", output);
        Assert.Contains($"Unreadable: {broken}", output);
        Assert.Single(second.Console.QuestionsAsked[0].Options);
        Assert.True(File.Exists(broken));
    }

    [Fact]
    public async Task Resume_WithNothingSaved_SaysSo()
    {
        using Showcase showcase = new();

        await showcase.RunAsync(new ScriptedChatClient(Text("never asked")), "/resume");

        Assert.Contains("There is no saved conversation to go on with", showcase.Console.Output);
        Assert.Empty(showcase.Console.QuestionsAsked);
    }

    [Fact]
    public async Task Undo_TakesBackAMessageThatEditsCreatesAndMoves_AndTheNextRequestHoldsTheNote()
    {
        using Showcase showcase = new();
        showcase.Write("a.txt", "one\n");
        showcase.Write("old.txt", "moved\n");
        ScriptedChatClient model = new(
            Edit("a.txt", "one", "two"),
            Call("CreateFile", ("path", "new.txt"), ("content", "created\n")),
            Call("MoveFile", ("sourcePath", "old.txt"), ("destinationPath", "renamed.txt")),
            Text("changed three files"), Text("nothing to check"),
            Text("understood"));

        await showcase.RunAsync(model, "/mode accept-edits", "change things", "/undo", "/undo", "what now?");

        Assert.Equal("one\n", File.ReadAllText(showcase.PathOf("a.txt")));
        Assert.False(File.Exists(showcase.PathOf("new.txt")));
        Assert.Equal("moved\n", File.ReadAllText(showcase.PathOf("old.txt")));
        Assert.False(File.Exists(showcase.PathOf("renamed.txt")));
        // The second undo had nothing left and said so.
        Assert.Contains("There is nothing to undo", showcase.Console.Output);
        // The model is told what is back as it was, in front of the prompt that follows.
        ChatMessage next = model.Requests[^1].Last(message => message.Text.EndsWith("what now?", StringComparison.Ordinal));
        Assert.StartsWith("Undo took back the file changes made for one user message.", next.Text);
        Assert.Contains("- a.txt: its previous content was put back", next.Text);
        Assert.Contains("- new.txt: removed; it had been created", next.Text);
        Assert.Contains("- old.txt: moved back from 'renamed.txt'", next.Text);
    }

    [Fact]
    public async Task TheStatusLine_ShowsTheModelTheModeAndTheTokens_SummedFromTheEvents()
    {
        using Showcase showcase = new();
        ScriptedChatClient model = new(
            new ChatResponse(Text("first")) { Usage = new UsageDetails { TotalTokenCount = 1200 } },
            new ChatResponse(Text("second")) { Usage = new UsageDetails { TotalTokenCount = 345 } });

        await showcase.RunAsync(model, "hello", "/mode plan", "again", "/status");

        string output = showcase.Console.Output;
        Assert.Contains("test-model · ask · run 1,200 tokens · session 1,200 tokens", output);
        Assert.Contains("test-model · plan · run 345 tokens · session 1,545 tokens", output);
        Assert.Contains("Tokens: 345 in the last run, 1,545 in this conversation", output);
        Assert.Contains("compacted on request only", output);
        Assert.Contains("Permission mode: plan.", output);
    }

    [Fact]
    public async Task Status_ListsTheProjectsRules_AndTheOnesAddedAtAPrompt()
    {
        using Showcase showcase = new();
        showcase.Config("""{ "allow": [ { "tool": "RunShell", "argument": "command", "prefix": "git status" } ], "verifyCommand": "make test" }""");
        showcase.Console.Approvals.Enqueue(ApprovalChoice.AllowForSession);
        ScriptedChatClient model = new(Shell("echo -n hi"), Text("ran"));

        await showcase.RunAsync(model, "run it", "/status");

        string output = showcase.Console.Output;
        Assert.Contains("Allowed by the project: RunShell starting with \"git status\"", output);
        Assert.Contains("Allowed by the project: RunShell with exactly \"make test\"", output);
        Assert.Contains("Allowed for this conversation: RunShell starting with \"echo\"", output);
        Assert.Contains("Verify command: make test", output);
    }
}
