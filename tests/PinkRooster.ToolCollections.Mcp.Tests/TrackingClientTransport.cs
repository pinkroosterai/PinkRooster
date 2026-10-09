using System.Threading.Channels;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PinkRooster.ToolCollections.Mcp.Tests;

/// <summary>Passes everything to another client transport, and records whether the client closed the session it opened.</summary>
internal sealed class TrackingClientTransport(IClientTransport inner) : IClientTransport
{
    public string Name => inner.Name;

    public bool Closed { get; private set; }

    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default) =>
        new Session(await inner.ConnectAsync(cancellationToken), this);

    private sealed class Session(ITransport inner, TrackingClientTransport owner) : ITransport
    {
        public string? SessionId => inner.SessionId;

        public ChannelReader<JsonRpcMessage> MessageReader => inner.MessageReader;

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) =>
            inner.SendMessageAsync(message, cancellationToken);

        public ValueTask DisposeAsync()
        {
            owner.Closed = true;
            return inner.DisposeAsync();
        }
    }
}
