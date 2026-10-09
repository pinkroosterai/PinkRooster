using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PinkRooster.ToolCollections.Mcp.Tests;

/// <summary>A real MCP server in this process, reached over two in-memory pipes, with a client connected to it or a transport to connect over.</summary>
internal sealed class InProcessMcpServer : IAsyncDisposable
{
    private readonly Pipe serverToClient;
    private readonly CancellationTokenSource stop;
    private readonly McpServer server;
    private readonly Task running;
    private McpClient? client;

    private InProcessMcpServer(Pipe serverToClient, CancellationTokenSource stop, McpServer server, Task running, TrackingClientTransport transport)
    {
        this.serverToClient = serverToClient;
        this.stop = stop;
        this.server = server;
        this.running = running;
        Transport = transport;
    }

    /// <summary>The client <see cref="StartAsync"/> connected; a server from <see cref="Start"/> has none.</summary>
    public McpClient Client => client ?? throw new InvalidOperationException("This server was started with Start; connect over Transport.");

    /// <summary>The server itself, to read what the client told it.</summary>
    public McpServer Server => server;

    /// <summary>The one transport to the server; it records whether the client that used it closed it.</summary>
    public TrackingClientTransport Transport { get; }

    /// <summary>Starts a server with these options and leaves connecting to the caller, over <see cref="Transport"/>.</summary>
    public static InProcessMcpServer Start(McpServerOptions options)
    {
        Pipe clientToServer = new();
        Pipe serverToClient = new();
        McpServer server = McpServer.Create(new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()), options);
        CancellationTokenSource stop = new();
        Task running = server.RunAsync(stop.Token);
        TrackingClientTransport transport = new(new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));
        return new InProcessMcpServer(serverToClient, stop, server, running, transport);
    }

    /// <summary>Starts a server with these options and connects a client to it.</summary>
    public static async Task<InProcessMcpServer> StartAsync(McpServerOptions options)
    {
        InProcessMcpServer started = Start(options);
        started.client = await McpClient.CreateAsync(started.Transport);
        return started;
    }

    /// <summary>Ends the server's side of the connection, as a server process that exits does: the client reads the end of its stream.</summary>
    public async Task DisconnectAsync()
    {
        await serverToClient.Writer.CompleteAsync();
        await stop.CancelAsync();
    }

    /// <summary>Server options serving the given tools, with optional instructions.</summary>
    public static McpServerOptions Serving(string? instructions, params McpServerTool[] tools)
    {
        McpServerOptions options = new() { ServerInstructions = instructions, ToolCollection = [] };
        foreach (McpServerTool tool in tools)
        {
            options.ToolCollection.Add(tool);
        }
        return options;
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
        }
        await stop.CancelAsync();
        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
        }
        await server.DisposeAsync();
        stop.Dispose();
    }
}
