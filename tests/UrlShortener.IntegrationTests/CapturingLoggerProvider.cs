using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace UrlShortener.IntegrationTests;

/// <summary>Keeps every log entry in memory so tests can check what was logged, and what was not.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    public sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception)
    {
        /// <summary>The message and the exception text: everything a log sink would receive.</summary>
        public string FullText => Exception is null ? Message : $"{Message} {Exception}";
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
    }
}