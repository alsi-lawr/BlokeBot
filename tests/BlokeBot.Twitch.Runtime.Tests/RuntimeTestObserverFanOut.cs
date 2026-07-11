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

    private sealed class TestReporter : IObserverFailureDiagnosticReporter
    {
        private readonly List<ObserverFailureDiagnosticReport> reports = [];

        public ValueTask ReportAsync(
            ObserverFailureDiagnosticReport report,
            CancellationToken cancellationToken
        )
        {
            reports.Add(report);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestCorrelationIdProvider : IObserverCorrelationIdProvider
    {
        private int next;

        public ObserverCorrelationId Next() =>
            ObserverCorrelationId.Named($"runtime-test-{++next}");
    }
}
