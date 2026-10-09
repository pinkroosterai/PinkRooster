using Microsoft.Extensions.Logging;

namespace PinkRooster.Agents.Tests.TestSupport;

/// <summary>Hands out one shared <see cref="RecordingLogger"/> for every category.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public RecordingLogger Logger { get; } = new();

    public ILogger CreateLogger(string categoryName) => Logger;

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }
}
