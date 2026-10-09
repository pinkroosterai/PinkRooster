using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PinkRooster.Samples.Tests.TestSupport;

/// <summary>A real MCP server in this process, reached over two in-memory pipes, with a client connected to it. The same idea as the one in the MCP package's tests.</summary>
internal sealed class InProcessMcpServer : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task running;

    private InProcessMcpServer(McpClient client, Task running)
    {
        Client = client;
        this.running = running;
    }

    public McpClient Client { get; }

    public static async Task<InProcessMcpServer> StartAsync(string? instructions, params McpServerTool[] tools)
    {
        McpServerOptions options = new() { ServerInstructions = instructions, ToolCollection = [] };
        foreach (McpServerTool tool in tools)
        {
            options.ToolCollection.Add(tool);
        }

        Pipe clientToServer = new();
        Pipe serverToClient = new();
        McpServer server = McpServer.Create(new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()), options);
        CancellationTokenSource source = new();
        Task running = server.RunAsync(source.Token);
        McpClient client = await McpClient.CreateAsync(new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
        InProcessMcpServer started = new(client, running);
        return started;
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await stop.CancelAsync();
        try
        {
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
            // The server stops with its client; a slow stop is not a failure of the test.
        }
    }
}
