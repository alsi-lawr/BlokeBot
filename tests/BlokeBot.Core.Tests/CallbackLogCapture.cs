using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Core.Tests;

internal sealed class CallbackLogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<CallbackLogEntry> _entries = [];

    public IReadOnlyCollection<CallbackLogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(categoryName, _entries);
    }

    public void Dispose() { }

    private sealed class Logger(string categoryName, ConcurrentQueue<CallbackLogEntry> entries)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            entries.Enqueue(new(categoryName, formatter(state, exception), properties));
        }
    }
}

internal sealed record CallbackLogEntry(
    string Category,
    string Message,
    IReadOnlyDictionary<string, object?> Properties
);
