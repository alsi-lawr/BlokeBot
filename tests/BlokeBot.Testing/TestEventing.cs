using BlokeBot.Eventing;

namespace BlokeBot.Testing;

public static class TestEventBus
{
    public static EventBus<TKey> Create<TKey>()
        where TKey : notnull
    {
        return Create<TKey>(key =>
            ObserverEventIdentity.Named($"Test.{typeof(TKey).Name}.{RequireKeyText(key)}")
        );
    }

    public static EventBus<TKey> Create<TKey>(Func<TKey, ObserverEventIdentity> eventIdentity)
        where TKey : notnull
    {
        var fanOut = TestObserverFanOut.FailOnObserverFailure<
            EventBusObserverBoundary<TKey>,
            EventNotification<TKey>,
            EventBusDeadLetter
        >(ObserverBoundary.Named($"Test.{typeof(TKey).Name}.Events"));
        return new EventBus<TKey>(
            fanOut,
            new EventBusEventIdentity<TKey> { Project = eventIdentity }
        );
    }

    public static TestEventBusRecording<TKey> CreateContinueAndRecord<TKey>()
        where TKey : notnull
    {
        return CreateContinueAndRecord<TKey>(key =>
            ObserverEventIdentity.Named($"Test.{typeof(TKey).Name}.{RequireKeyText(key)}")
        );
    }

    public static TestEventBusRecording<TKey> CreateContinueAndRecord<TKey>(
        Func<TKey, ObserverEventIdentity> eventIdentity
    )
        where TKey : notnull
    {
        var recording = TestObserverFanOut.ContinueAndRecord<
            EventBusObserverBoundary<TKey>,
            EventNotification<TKey>,
            EventBusDeadLetter
        >(ObserverBoundary.Named($"Test.{typeof(TKey).Name}.Events"));
        return new(
            new EventBus<TKey>(
                recording.FanOut,
                new EventBusEventIdentity<TKey> { Project = eventIdentity }
            ),
            recording.Reports
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
    public static ObserverFanOut<TBoundary, TEvent, TDeadLetter> FailOnObserverFailure<
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
            new FailingObserverFailureReporter(),
            new TestObserverCorrelationIdProvider()
        );
    }

    public static TestObserverFanOutRecording<TBoundary, TEvent, TDeadLetter> ContinueAndRecord<
        TBoundary,
        TEvent,
        TDeadLetter
    >(ObserverBoundary boundary)
        where TDeadLetter : IObserverDeadLetterPayload
    {
        var reporter = new RecordingObserverFailureReporter();
        return new(
            new(
                new ObserverFailurePolicy<TBoundary, TDeadLetter>.ContinueAndReport
                {
                    Boundary = boundary,
                },
                reporter,
                new TestObserverCorrelationIdProvider()
            ),
            reporter.Reports
        );
    }

    private sealed class FailingObserverFailureReporter : IObserverFailureDiagnosticReporter
    {
        public ValueTask ReportAsync(
            ObserverFailureDiagnosticReport report,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromException(
                new InvalidOperationException("A test event observer failed.")
            );
        }
    }

    private sealed class RecordingObserverFailureReporter : IObserverFailureDiagnosticReporter
    {
        public List<ObserverFailureSummary> Reports { get; } = [];

        public ValueTask ReportAsync(
            ObserverFailureDiagnosticReport report,
            CancellationToken cancellationToken
        )
        {
            Reports.Add(report.Summary);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestObserverCorrelationIdProvider : IObserverCorrelationIdProvider
    {
        private int _next;

        public ObserverCorrelationId Next()
        {
            return ObserverCorrelationId.Named($"test-correlation-{++_next}");
        }
    }
}

public sealed record TestEventBusRecording<TKey>(
    EventBus<TKey> Events,
    IReadOnlyList<ObserverFailureSummary> Reports
)
    where TKey : notnull;

public sealed record TestObserverFanOutRecording<TBoundary, TEvent, TDeadLetter>(
    ObserverFanOut<TBoundary, TEvent, TDeadLetter> FanOut,
    IReadOnlyList<ObserverFailureSummary> Reports
)
    where TDeadLetter : IObserverDeadLetterPayload;
