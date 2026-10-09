using Microsoft.Extensions.Logging;

namespace PinkRooster.Agents.Tests.TestSupport;

/// <summary>Keeps the log entries: the Error-level ones in <see cref="Errors"/>, all of them in <see cref="Entries"/>.</summary>
internal sealed class RecordingLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Errors { get; } = [];

    /// <summary>Every entry of every level, with its exception. Read it after the logging is over; entries are added under a lock.</summary>
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
            if (logLevel == LogLevel.Error) Errors.Add((logLevel, formatter(state, exception)));
        }
    }
}
