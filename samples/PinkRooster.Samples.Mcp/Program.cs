using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using PinkRooster.Agents;
using PinkRooster.Agents.Eventing;
using PinkRooster.Samples.Terminal;
using PinkRooster.SpectreConsole;
using PinkRooster.ToolCollections;
using PinkRooster.ToolCollections.Mcp;

namespace PinkRooster.Samples.Mcp;

public static class Program
{
    /// <summary>The sample asks the user to approve tools of the file server, so it needs an interactive terminal.</summary>
    public const bool NeedsTerminal = true;

    /// <summary>The resource whose text goes into the file server's context on every model call, when the server has it.</summary>
    private const string ProjectResource = "project://summary";

    public static Task<int> Main() => SampleHost.RunAsync(RunAsync, NeedsTerminal, usesTools: true);

    /// <summary>How the two collections are connected; the tests replace it with servers that run in the test process.</summary>
    public sealed record Servers(Func<CancellationToken, Task<McpToolCollection>> Docs, Func<CancellationToken, Task<McpToolCollection>> Files);

    public static Task RunAsync(IChatClient chatClient, ISampleConsole console, CancellationToken cancellationToken) =>
        RunAsync(chatClient, console, new Servers(ConnectDocsAsync, ConnectFilesAsync), cancellationToken);

    public static async Task RunAsync(IChatClient chatClient, ISampleConsole console, Servers servers, CancellationToken cancellationToken)
    {
        // Each collection owns its client; disposing it disconnects. A server that is not there is skipped.
        await using McpToolCollection? docs = await TryConnectAsync(console, "Context7", servers.Docs, cancellationToken);
        await using McpToolCollection? files = await TryConnectAsync(console, "Files", servers.Files, cancellationToken);
        List<ToolCollection> collections = [];
        if (docs is not null)
        {
            collections.Add(docs.WithConstraint("Name the library version you looked up."));
        }
        if (files is not null)
        {
            bool reported = false;
            collections.Add(files
                // Every tool the server does not mark read-only or non-destructive asks first. The hints can only add approval.
                .RequireApprovalForDestructiveTools()
                // Read before every model call, so each call costs one more round trip to the server.
                .WithContext(async token =>
                {
                    try
                    {
                        ReadResourceResult resource = await files.Client.ReadResourceAsync(ProjectResource, cancellationToken: token);
                        return $"## Project\n{resource.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text}";
                    }
                    catch (McpException error)
                    {
                        if (!reported)
                        {
                            reported = true;
                            console.WriteLine($"The file server has no {ProjectResource} resource, so its context stays empty: {error.Message}");
                        }
                        return null;
                    }
                }));
        }
        if (collections.Count == 0)
        {
            console.WriteLine("No MCP server could be reached, so there is nothing to show. See the messages above.");
            return;
        }

        AIAgent agent = chatClient
            .CreateAgent()
            .WithRole("You answer questions about this project and the libraries it uses.")
            .WithTools([.. collections])
            .OnEvent<ToolCallStarted>(call => console.WriteLine($"-> {call.Name}"))
            .Build();

        AgentSession session = await agent.CreateSessionAsync(cancellationToken);
        AgentResponse response = await agent.RunWithApprovalsAsync(
            "Look up how to stream a chat response with Microsoft.Extensions.AI, then list the project's markdown files.",
            session, console.ConfirmToolCallAsync, cancellationToken: cancellationToken);

        console.WriteAnswer(response.Text);
    }

    // A remote server over HTTP. The key is optional: a header whose value is null or blank is left out.
    private static Task<McpToolCollection> ConnectDocsAsync(CancellationToken cancellationToken) =>
        McpToolCollection.ConnectHttpAsync(
            "Context7",
            new Uri("https://mcp.context7.com/mcp"),
            new Dictionary<string, string?> { ["CONTEXT7_API_KEY"] = Environment.GetEnvironmentVariable("CONTEXT7_API_KEY") },
            cancellationToken: cancellationToken);

    // A local server process over standard input and output. select prefixes every name, so this server's tools cannot clash with another's.
    private static Task<McpToolCollection> ConnectFilesAsync(CancellationToken cancellationToken) =>
        McpToolCollection.ConnectStdioAsync(
            "Files",
            "npx",
            ["-y", "@modelcontextprotocol/server-filesystem", Directory.GetCurrentDirectory()],
            select: tool => tool.WithName($"files_{tool.Name}"),
            cancellationToken: cancellationToken);

    private static async Task<McpToolCollection?> TryConnectAsync(ISampleConsole console, string server, Func<CancellationToken, Task<McpToolCollection>> connect, CancellationToken cancellationToken)
    {
        try
        {
            return await connect(cancellationToken);
        }
        catch (McpException error)
        {
            // Connecting failed with a message that names the server and what to check.
            console.WriteLine($"{server} is unavailable, so the sample runs without it: {error.Message}");
            return null;
        }
    }
}
