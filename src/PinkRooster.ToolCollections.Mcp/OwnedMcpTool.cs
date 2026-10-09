using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace PinkRooster.ToolCollections.Mcp;

/// <summary>
/// A tool of an MCP collection that owns its client. Once the collection is disposed, a call fails at once with
/// <see cref="ObjectDisposedException"/>, and a call still waiting for the server is stopped with the same exception.
/// </summary>
/// <remarks>
/// The SDK's client does not fail a call after it is disposed: the call waits until its own token fires, which in an agent run
/// without a deadline is never.
/// </remarks>
internal sealed class OwnedMcpTool(McpClientTool tool, string collection, CancellationToken disposed) : DelegatingAIFunction(tool)
{
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (disposed.IsCancellationRequested)
        {
            throw Disposed();
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposed);
        try
        {
            return await base.InvokeCoreAsync(arguments, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (disposed.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw Disposed();
        }
    }

    private ObjectDisposedException Disposed() =>
        new(collection, $"Tool '{Name}' was called after the MCP collection '{collection}' was disposed, which disconnected its client; connect a new collection and build a new agent.");
}
