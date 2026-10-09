using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PinkRooster.Agents;

namespace PinkRooster.ToolCollections.Mcp.Tests;

/// <summary>
/// Keeps the README's MCP examples compiling, running and matching what the README shows; the main example and its one connect line
/// stay within 8 lines to the first answer.
/// </summary>
public sealed class McpReadmeTests
{
    // The README's connect line comes before the block, so the block gets one line less than the 8.
    private const int LineBudget = 7;

    [Fact]
    public async Task McpExample_RunsAgainstAServerAndAChatClient()
    {
        await using InProcessMcpServer server = InProcessMcpServer.Start(InProcessMcpServer.Serving(
            "Look a library up before answering.",
            McpServerTool.Create((string library) => $"docs for {library}", new() { Name = "query-docs" })));
        OneReplyChatClient chatClient = new("Use GetStreamingResponseAsync.");
        StringWriter output = new();
        TextWriter original = Console.Out;
        Console.SetOut(output);
        try
        {
            await using McpToolCollection docs = await McpToolCollection.ConnectAsync("Context7", server.Transport, cancellationToken: TestContext.Current.CancellationToken);
            await RunMcpExample(chatClient, docs);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal("Use GetStreamingResponseAsync.", output.ToString().Trim());
        Assert.Contains("Look a library up before answering.", chatClient.Instructions);
        Assert.Contains("Name the library version you looked up.", chatClient.Instructions);
        Assert.Equal(["query-docs"], chatClient.ToolNames);
    }

    [Fact]
    public void Readme_ShowsTheCompiledMcpExample_WithinItsBudget()
    {
        string[] readme = ReadmeBlock("mcp-example");

        Assert.Equal(SourceBlock("mcp-example"), readme);
        Assert.InRange(readme.Length, 1, LineBudget);
    }

    [Fact]
    public async Task ResourceContextPattern_ReadsTheResourceIntoTheContext()
    {
        McpServerOptions options = InProcessMcpServer.Serving(null);
        options.ResourceCollection = [McpServerResource.Create(() => "main", new() { UriTemplate = "git://current-branch", Name = "current-branch" })];
        await using InProcessMcpServer server = await InProcessMcpServer.StartAsync(options);

        McpToolCollection git = await ResourceContextPattern(server.Client);

        Assert.Equal("## Git\nBranch: main", await git.GetContextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Readme_ShowsTheCompiledResourceContextPattern()
    {
        Assert.Equal(SourceBlock("mcp-resource-context"), ReadmeBlock("mcp-resource-context"));
    }

    // The main MCP example exactly as the README shows it, after its one connect line.
    private static async Task RunMcpExample(IChatClient chatClient, McpToolCollection docs)
    {
        // mcp-example:begin
        var agent = chatClient
            .CreateAgent()
            .WithRole("You answer questions about libraries from their current documentation.")
            .WithTools(docs.WithConstraint("Name the library version you looked up."))
            .Build();
        var response = await agent.RunAsync("How do I stream a chat response?");
        Console.WriteLine(response.Text);
        // mcp-example:end
    }

    // The resource-context pattern exactly as the README shows it.
    private static async Task<McpToolCollection> ResourceContextPattern(McpClient gitServer)
    {
        // mcp-resource-context:begin
        var git = await McpToolCollection.CreateAsync("Git", gitServer);
        git.WithContext(async ct =>
        {
            var branch = await gitServer.ReadResourceAsync("git://current-branch", cancellationToken: ct);
            return $"## Git\nBranch: {branch.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text}";
        });
        // mcp-resource-context:end
        return git;
    }

    private static string[] ReadmeBlock(string marker)
    {
        string readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "PinkRooster.ToolCollections.Mcp.md")).ReplaceLineEndings("\n");
        Match block = Regex.Match(readme, $@"<!-- {marker}:begin -->\s*```csharp\n(?<code>.*?)```\s*<!-- {marker}:end -->", RegexOptions.Singleline);
        Assert.True(block.Success, $"docs/PinkRooster.ToolCollections.Mcp.md has no block between the {marker} markers.");
        return NonBlank(block.Groups["code"].Value);
    }

    private static string[] SourceBlock(string marker)
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tests", "PinkRooster.ToolCollections.Mcp.Tests", "McpReadmeTests.cs")).ReplaceLineEndings("\n");
        Match body = Regex.Match(source, $@"// {marker}:begin\n(?<code>.*?)\s*// {marker}:end", RegexOptions.Singleline);
        Assert.True(body.Success, $"McpReadmeTests.cs lost its {marker} markers.");
        return NonBlank(body.Groups["code"].Value);
    }

    private static string[] NonBlank(string code) =>
        [.. code.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("using ", StringComparison.Ordinal))];

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PinkRooster.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("PinkRooster.slnx not found above the test output folder.");
    }
}
