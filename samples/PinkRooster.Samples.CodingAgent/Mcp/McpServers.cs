using PinkRooster.Samples.CodingAgent.Project;
using PinkRooster.ToolCollections.Mcp;

namespace PinkRooster.Samples.CodingAgent.Mcp;

/// <summary>Connects the MCP servers of the config file. Each becomes a tool collection whose tools ask first unless the server marks them safe.</summary>
public static class McpServers
{
    /// <summary>How long a server gets to connect at start.</summary>
    public static readonly TimeSpan ConnectLimit = TimeSpan.FromSeconds(5);

    /// <summary>The server the program uses when the config file names none: documentation of libraries, over the network.</summary>
    public static McpServer Context7 { get; } = new("Context7", new Uri("https://mcp.context7.com/mcp"), null, []);

    /// <summary>Connects one server: over HTTP for a URL, as a child process for a command.</summary>
    public static Task<McpToolCollection> ConnectAsync(McpServer server, CancellationToken cancellationToken) => server.Url is Uri url
        ? McpToolCollection.ConnectHttpAsync(server.Name, url, cancellationToken: cancellationToken)
        : McpToolCollection.ConnectStdioAsync(server.Name, server.Command!, server.Arguments, cancellationToken: cancellationToken);

    /// <summary>
    /// Connects every server, each within <see cref="ConnectLimit"/>. A server that cannot be reached is left out, with one line
    /// to <paramref name="warn"/> that names it; the program works without it.
    /// </summary>
    public static async Task<IReadOnlyList<McpToolCollection>> ConnectAllAsync(
        IReadOnlyList<McpServer> servers,
        Func<McpServer, CancellationToken, Task<McpToolCollection>> connect,
        Action<string> warn,
        CancellationToken cancellationToken)
    {
        // All at once, so several slow servers together cost one time limit and not one each.
        Task<McpToolCollection?>[] connecting = [.. servers.Select(server => TryConnectAsync(server, connect, warn, cancellationToken))];
        return [.. (await Task.WhenAll(connecting).ConfigureAwait(false)).OfType<McpToolCollection>()];
    }

    private static async Task<McpToolCollection?> TryConnectAsync(
        McpServer server,
        Func<McpServer, CancellationToken, Task<McpToolCollection>> connect,
        Action<string> warn,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(ConnectLimit);
        try
        {
            McpToolCollection collection = await connect(server, limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
            // Every tool the server does not mark read-only or non-destructive needs approval. The hints can only add approval.
            return collection.RequireApprovalForDestructiveTools();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            warn($"MCP server '{server.Name}' did not connect within {ConnectLimit.TotalSeconds:0} seconds and is left out.");
            return null;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            warn($"MCP server '{server.Name}' could not be reached and is left out: {error.Message}");
            return null;
        }
    }
}
