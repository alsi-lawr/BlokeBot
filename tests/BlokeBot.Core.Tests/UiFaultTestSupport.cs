using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Core.Tests;

internal sealed class CapturingErrorBoundary : ErrorBoundaryBase
{
    internal Exception? CapturedException { get; private set; }

    protected override Task OnErrorAsync(Exception exception)
    {
        CapturedException = exception;
        return Task.CompletedTask;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (CapturedException is null)
        {
            builder.AddContent(0, ChildContent);
        }
        else
        {
            builder.AddContent(1, "failed");
        }
    }
}

internal sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    internal List<UiFaultLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

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
            : [];
        Entries.Add(new(logLevel, exception, formatter(state, exception), properties));
    }
}

internal sealed record UiFaultLogEntry(
    LogLevel Level,
    Exception? Exception,
    string Message,
    IReadOnlyDictionary<string, object?> Properties
);
