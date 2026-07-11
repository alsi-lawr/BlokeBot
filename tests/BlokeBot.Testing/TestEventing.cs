using BlokeBot.Eventing;

namespace BlokeBot.Testing;

public static class TestEventBus
{
    public static EventBus<TKey> Create<TKey>()
        where TKey : notnull =>
        Create<TKey>(key =>
            ObserverEventIdentity.Named(
                $"Test.{typeof(TKey).Name}.{RequireKeyText(key)}"
            )
        );

    public static EventBus<TKey> Create<TKey>(
        Func<TKey, ObserverEventIdentity> eventIdentity
    )
        where TKey : notnull
    {
        var fanOut = TestObserverFanOut.Continue<
            EventBusObserverBoundary<TKey>,
            EventNotification<TKey>,
            EventBusDeadLetter
        >(ObserverBoundary.Named($"Test.{typeof(TKey).Name}.Events"));
        return new EventBus<TKey>(
            fanOut,
            new EventBusEventIdentity<TKey> { Project = eventIdentity }
        );
    }

    private static string RequireKeyText<TKey>(TKey key)
    {
        var text = key?.ToString();
        return string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("Test event keys must have a stable identity.")
            : text;
    }
}

public static class TestObserverFanOut
{
    public static ObserverFanOut<TBoundary, TEvent, TDeadLetter> Continue<
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
            new TestObserverFailureReporter(),
            new TestObserverCorrelationIdProvider()
        );

    private sealed class TestObserverFailureReporter
        : IObserverFailureDiagnosticReporter
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

    private sealed class TestObserverCorrelationIdProvider
        : IObserverCorrelationIdProvider
    {
        private int next;

        public ObserverCorrelationId Next() =>
            ObserverCorrelationId.Named($"test-correlation-{++next}");
    }
}
