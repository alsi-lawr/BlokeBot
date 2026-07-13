using BlokeBot.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlokeBot.Twitch.Runtime.Tests;

internal static class RuntimeTestObserverFanOut
{
    internal static ObserverFanOut<TBoundary, TEvent, TDeadLetter> Continue<
        TBoundary,
        TEvent,
        TDeadLetter
    >(ObserverBoundary boundary)
        where TDeadLetter : IObserverDeadLetterPayload
    {
        return new(
            new ObserverFailurePolicy<TBoundary, TDeadLetter>.ContinueAndReport
            {
                Boundary = boundary,
            },
            NullLogger<ObserverFanOut<TBoundary, TEvent, TDeadLetter>>.Instance,
            new TestCorrelationIdProvider()
        );
    }

    internal static ObserverFanOut<TBoundary, TEvent, TDeadLetter> EscalatingContinue<
        TBoundary,
        TEvent,
        TDeadLetter
    >(ObserverBoundary boundary, Exception loggingFailure)
        where TDeadLetter : IObserverDeadLetterPayload
    {
        return new(
            new ObserverFailurePolicy<TBoundary, TDeadLetter>.ContinueAndReport
            {
                Boundary = boundary,
            },
            new ThrowingLogger<ObserverFanOut<TBoundary, TEvent, TDeadLetter>>(
                loggingFailure
            ),
            new TestCorrelationIdProvider()
        );
    }

    private sealed class TestCorrelationIdProvider : IObserverCorrelationIdProvider
    {
        private int _next;

        public ObserverCorrelationId Next()
        {
            return ObserverCorrelationId.Named($"runtime-test-{++_next}");
        }
    }

    private sealed class ThrowingLogger<TCategory>(Exception failure)
        : ILogger<TCategory>
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
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
            throw failure;
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose() { }
    }
}
