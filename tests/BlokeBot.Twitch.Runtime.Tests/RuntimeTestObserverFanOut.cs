using BlokeBot.Eventing;

namespace BlokeBot.Twitch.Runtime.Tests;

internal static class RuntimeTestObserverFanOut
{
    internal static ObserverFanOut<TBoundary, TEvent, TDeadLetter> Continue<
        TBoundary,
        TEvent,
        TDeadLetter
    >(ObserverBoundary boundary)
        where TDeadLetter : IObserverDeadLetterPayload =>
        new(
            new ObserverFailurePolicy<TBoundary, TDeadLetter>.ContinueAndReport
            {
                Boundary = boundary,
            },
            new TestReporter(),
            new TestCorrelationIdProvider()
        );

    internal static ObserverFanOut<TBoundary, TEvent, TDeadLetter> EscalatingContinue<
        TBoundary,
        TEvent,
        TDeadLetter
    >(ObserverBoundary boundary, Exception reporterFailure)
        where TDeadLetter : IObserverDeadLetterPayload =>
        new(
            new ObserverFailurePolicy<TBoundary, TDeadLetter>.ContinueAndReport
            {
                Boundary = boundary,
            },
            new ThrowingReporter(reporterFailure),
            new TestCorrelationIdProvider()
        );

    private sealed class TestReporter : IObserverFailureDiagnosticReporter
    {
        private readonly List<ObserverFailureDiagnosticReport> _reports = [];

        public ValueTask ReportAsync(
            ObserverFailureDiagnosticReport report,
            CancellationToken cancellationToken
        )
        {
            _reports.Add(report);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestCorrelationIdProvider : IObserverCorrelationIdProvider
    {
        private int _next;

        public ObserverCorrelationId Next() =>
            ObserverCorrelationId.Named($"runtime-test-{++_next}");
    }

    private sealed class ThrowingReporter(Exception failure) : IObserverFailureDiagnosticReporter
    {
        public ValueTask ReportAsync(
            ObserverFailureDiagnosticReport report,
            CancellationToken cancellationToken
        ) => ValueTask.FromException(failure);
    }
}
