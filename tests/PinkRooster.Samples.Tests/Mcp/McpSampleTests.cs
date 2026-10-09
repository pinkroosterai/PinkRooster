using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PinkRooster.Agents.Tests.TestSupport;
using PinkRooster.Samples.Tests.TestSupport;
using PinkRooster.ToolCollections.Mcp;
using static PinkRooster.Samples.Tests.TestSupport.Replies;
using Sample = PinkRooster.Samples.Mcp.Program;

namespace PinkRooster.Samples.Tests.Mcp;

public sealed class McpSampleTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<McpToolCollection> DocsAsync(CancellationToken token)
    {
        InProcessMcpServer server = await InProcessMcpServer.StartAsync(
            "Look libraries up here.",
            McpServerTool.Create(([Description("What to look for.")] string query) => $"docs for {query}", new() { Name = "search_docs", ReadOnly = true }));
        return await McpToolCollection.CreateAsync("Context7", server.Client, cancellationToken: token);
    }

    private static async Task<McpToolCollection> FilesAsync(CancellationToken token)
    {
        InProcessMcpServer server = await InProcessMcpServer.StartAsync(
            null,
            McpServerTool.Create(() => "README.md", new() { Name = "list_files", ReadOnly = true }),
            McpServerTool.Create(() => "written", new() { Name = "write_file", ReadOnly = false, Destructive = true }));
        return await McpToolCollection.CreateAsync("Files", server.Client, tool => tool.WithName($"files_{tool.Name}"), cancellationToken: token);
    }

    private static Task<McpToolCollection> Unreachable(CancellationToken token) => throw new McpException("Could not connect to MCP server 'Test': refused.");

    [Fact]
    public async Task BothServersGiveTheAgentTheirTools_RenamedAndWithApprovalWhereTheServerDoesNotSayReadOnly()
    {
        ScriptedChatClient model = new(
            Call("c1", "search_docs", new() { ["query"] = "streaming" }),
            Call("c2", "files_write_file"),
            Text("Streaming works with GetStreamingResponseAsync; the project has README.md."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, new Sample.Servers(DocsAsync, FilesAsync), Token);

        string[] tools = [.. model.Options[0]!.Tools!.Select(tool => tool.Name)];
        Assert.Contains("search_docs", tools);
        Assert.Contains("files_list_files", tools);
        Assert.Contains("files_write_file", tools);
        Assert.Contains("Look libraries up here.", model.Options[0]!.Instructions);
        Assert.Contains("Name the library version you looked up.", model.Options[0]!.Instructions);
        // Only the tool the server does not mark read-only asks first.
        Assert.Equal(["files_write_file"], console.ApprovalsAsked);
        Assert.Contains("-> search_docs", console.Output);
        // The test server has no project resource, so the sample says so once and goes on.
        Assert.Equal(1, console.Output.Split("has no project://summary resource").Length - 1);
        Assert.Contains("README.md", console.Output);
    }

    [Fact]
    public async Task AServerThatCannotBeReached_IsReportedAndSkipped()
    {
        ScriptedChatClient model = new(Text("Only the docs."));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, new Sample.Servers(DocsAsync, Unreachable), Token);

        Assert.Contains("Files is unavailable, so the sample runs without it", console.Output);
        Assert.DoesNotContain(model.Options[0]!.Tools!, tool => tool.Name.StartsWith("files_", StringComparison.Ordinal));
        Assert.Contains("Only the docs.", console.Output);
    }

    [Fact]
    public async Task WithNoServerReachable_SaysSo_AndCallsNoModel()
    {
        ScriptedChatClient model = new(Text("never asked"));
        RecordingConsole console = new();

        await Sample.RunAsync(model, console, new Sample.Servers(Unreachable, Unreachable), Token);

        Assert.Empty(model.Requests);
        Assert.Contains("No MCP server could be reached", console.Output);
    }

    [Fact]
    public async Task WithoutAModelServer_ExitsWithOne_AndSaysWhereToLook() =>
        await SampleChecks.AssertExitsNamingModelsFile((client, screen, token) => Sample.RunAsync(client, screen, new Sample.Servers(DocsAsync, FilesAsync), token), needsTerminal: true);

    [Fact]
    public async Task WithoutATerminal_ExitsWithOne_AndSaysSo() =>
        await SampleChecks.AssertNeedsATerminal((client, screen, token) => Sample.RunAsync(client, screen, new Sample.Servers(DocsAsync, FilesAsync), token));
}
