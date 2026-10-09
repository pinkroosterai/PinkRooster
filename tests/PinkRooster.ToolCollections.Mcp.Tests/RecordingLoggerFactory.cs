using Microsoft.Extensions.Logging;

namespace PinkRooster.ToolCollections.Mcp.Tests;

/// <summary>Keeps every log entry, of every level and category, in the order they were written.</summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public List<(string Category, LogLevel Level, string Message)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }

    private sealed class Logger(RecordingLoggerFactory owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner.Entries)
            {
                owner.Entries.Add((category, logLevel, formatter(state, exception)));
            }
        }
    }
}
