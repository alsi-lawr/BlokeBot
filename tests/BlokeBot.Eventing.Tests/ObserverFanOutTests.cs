using Shouldly;

namespace BlokeBot.Eventing.Tests;

public sealed class ObserverFanOutTests
{
    private static readonly ObserverBoundary _boundary = ObserverBoundary.Named(
        "Test.ObserverFanOut"
    );
    private static readonly ObserverEventIdentity _eventIdentity = ObserverEventIdentity.Named(
        "TestEvent"
    );
    private static readonly ObserverCorrelationId _correlationId = ObserverCorrelationId.Named(
        "correlation-123"
    );

    [Test]
    public async Task ContinueAndReport_FailingObserver_ContinuesInOrderWithRedactedOutcome()
    {
        var failure = new InvalidOperationException("secret exception payload");
        var order = new List<string>();
        var reporter = new RecordingReporter();
        var observers = new[]
        {
            Observer("first", () => order.Add("first")),
            FailingObserver("failing", failure, order),
            Observer("third", () => order.Add("third")),
        };
        var fanOut = CreateFanOut(Continue(), reporter);

        var outcome = await DispatchAsync(fanOut, observers, CancellationToken.None);

        order.ShouldBe(["first", "failing", "third"]);
        var handled = outcome.ShouldBeOfType<ObserverFanOutOutcome.CompletedWithFailures>();
        var summary = handled.Failures.ShouldHaveSingleItem();
        AssertFailure(
            summary,
            "failing",
            attempt: 1,
            ObserverFailureClassification.Terminal,
            typeof(InvalidOperationException)
        );
        reporter.Reports.ShouldHaveSingleItem().Exception.ShouldBeSameAs(failure);
        handled.ToString().ShouldNotContain("secret exception payload");
    }

    [Test]
    public async Task BoundedRetry_TransientThenSuccess_RetriesOnlyFailedObserver()
    {
        var transient = new IOException("temporary observer failure");
        var order = new List<string>();
        var retryAttempt = 0;
        var reporter = new RecordingReporter();
        var observers = new[]
        {
            Observer("first", () => order.Add("first")),
            new TestObserver(
                "retrying",
                _ =>
                {
                    retryAttempt++;
                    order.Add($"retrying-{retryAttempt}");
                    return retryAttempt == 1
                        ? ValueTask.FromException(transient)
                        : ValueTask.CompletedTask;
                }
            ),
            Observer("third", () => order.Add("third")),
        };
        var fanOut = CreateFanOut(Retry(attemptLimit: 3), reporter);

        var outcome = await DispatchAsync(fanOut, observers, CancellationToken.None);

        order.ShouldBe(["first", "retrying-1", "retrying-2", "third"]);
        retryAttempt.ShouldBe(2);
        var failure = outcome
            .ShouldBeOfType<ObserverFanOutOutcome.CompletedWithFailures>()
            .Failures.ShouldHaveSingleItem();
        AssertFailure(
            failure,
            "retrying",
            attempt: 1,
            ObserverFailureClassification.Transient,
            typeof(IOException)
        );
        reporter.Reports.ShouldHaveSingleItem().Exception.ShouldBeSameAs(transient);
    }

    [Test]
    public async Task BoundedRetry_TransientExhaustion_UsesTotalAttemptLimitAndContinuesSibling()
    {
        var failures = new[]
        {
            new IOException("first"),
            new IOException("second"),
            new IOException("third"),
        };
        var attempts = 0;
        var laterCalled = false;
        var reporter = new RecordingReporter();
        var observers = new[]
        {
            new TestObserver("exhausting", _ => ValueTask.FromException(failures[attempts++])),
            Observer("later", () => laterCalled = true),
        };
        var fanOut = CreateFanOut(Retry(attemptLimit: 3), reporter);

        var outcome = await DispatchAsync(fanOut, observers, CancellationToken.None);

        attempts.ShouldBe(3);
        laterCalled.ShouldBeTrue();
        var handled = outcome.ShouldBeOfType<ObserverFanOutOutcome.CompletedWithFailures>();
        handled.Failures.Select(failure => failure.Attempt).ShouldBe([1, 2, 3]);
        handled.Failures.ShouldAllBe(failure =>
            failure.Classification == ObserverFailureClassification.Transient
        );
        reporter.Reports.Select(report => report.Exception).ShouldBe(failures);
    }

    [Test]
    public async Task BoundedRetry_TerminalFailure_DoesNotRetry()
    {
        var attempts = 0;
        var reporter = new RecordingReporter();
        var fanOut = CreateFanOut(Retry(attemptLimit: 4), reporter);
        var observer = new TestObserver(
            "terminal",
            _ =>
            {
                attempts++;
                return ValueTask.FromException(new InvalidOperationException("terminal"));
            }
        );

        var outcome = await DispatchAsync(fanOut, [observer], CancellationToken.None);

        attempts.ShouldBe(1);
        outcome
            .ShouldBeOfType<ObserverFanOutOutcome.CompletedWithFailures>()
            .Failures.ShouldHaveSingleItem()
            .Classification.ShouldBe(ObserverFailureClassification.Terminal);
    }

    [Test]
    public async Task DeadLetter_FailingObserver_StoresOneTypedRedactedRecord()
    {
        var observerFailure = new InvalidOperationException("private failure message");
        var reporter = new RecordingReporter();
        var sink = new RecordingDeadLetterSink();
        var fanOut = CreateFanOut(DeadLetter(sink), reporter);

        var outcome = await DispatchAsync(
            fanOut,
            [FailingObserver("dead-lettered", observerFailure, [])],
            CancellationToken.None
        );

        _ = outcome.ShouldBeOfType<ObserverFanOutOutcome.CompletedWithFailures>();
        sink.Attempts.ShouldBe(1);
        var deadLetter = sink.Entries.ShouldHaveSingleItem();
        deadLetter.Payload.ShouldBe(new TestDeadLetter("event-42"));
        deadLetter.Failure.Observer.ShouldBe(ObserverIdentity.Named("dead-lettered"));
        deadLetter.ToString().ShouldNotContain("private failure message");
    }

    [Test]
    public async Task ReporterFailure_AfterObserverFailure_ContinuesSiblingThenEscalatesOnce()
    {
        var observerFailure = new InvalidOperationException("observer failed");
        var reporterFailure = new IOException("reporter failed");
        var laterCalled = false;
        var reporter = new RecordingReporter();
        reporter.EnqueueFailure(reporterFailure);
        var fanOut = CreateFanOut(Continue(), reporter);

        var exception = await Should.ThrowAsync<ObserverFanOutEscalationException>(() =>
            DispatchAsync(
                    fanOut,
                    [
                        FailingObserver("failing", observerFailure, []),
                        Observer("later", () => laterCalled = true),
                    ],
                    CancellationToken.None
                )
                .AsTask()
        );

        laterCalled.ShouldBeTrue();
        reporter.Attempts.ShouldBe(1);
        exception.Causes.ShouldBe([observerFailure, reporterFailure]);
        exception
            .Failures.ShouldHaveSingleItem()
            .Observer.ShouldBe(ObserverIdentity.Named("failing"));
        var handlingFailure = exception.HandlingFailures.ShouldHaveSingleItem();
        handlingFailure.Stage.ShouldBe(ObserverFailureHandlingStage.Reporter);
        handlingFailure.FailureType.ShouldBe(typeof(IOException).FullName);
        exception.InnerException.ShouldBeNull();
    }

    [Test]
    public async Task ReporterAndDeadLetterFailures_ContinueSiblingAndRetainEveryExactFailure()
    {
        var observerFailure = new InvalidOperationException("observer failed");
        var reporterFailure = new IOException("reporter failed");
        var sinkFailure = new IOException("sink failed");
        var laterCalled = false;
        var reporter = new RecordingReporter();
        reporter.EnqueueFailure(reporterFailure);
        var sink = new RecordingDeadLetterSink();
        sink.EnqueueFailure(sinkFailure);
        var fanOut = CreateFanOut(DeadLetter(sink), reporter);

        var exception = await Should.ThrowAsync<ObserverFanOutEscalationException>(() =>
            DispatchAsync(
                    fanOut,
                    [
                        FailingObserver("failing", observerFailure, []),
                        Observer("later", () => laterCalled = true),
                    ],
                    CancellationToken.None
                )
                .AsTask()
        );

        laterCalled.ShouldBeTrue();
        reporter.Attempts.ShouldBe(1);
        sink.Attempts.ShouldBe(1);
        exception.Causes.ShouldBe([observerFailure, reporterFailure, sinkFailure]);
        _ = exception.Failures.ShouldHaveSingleItem();
        exception
            .HandlingFailures.Select(failure => failure.Stage)
            .ShouldBe([
                ObserverFailureHandlingStage.Reporter,
                ObserverFailureHandlingStage.DeadLetterSink,
            ]);
    }

    [Test]
    public async Task DispatchCancellation_FromObserver_PropagatesWithoutReportOrDeadLetter()
    {
        using var cancellation = new CancellationTokenSource();
        var laterCalled = false;
        var reporter = new RecordingReporter();
        var sink = new RecordingDeadLetterSink();
        var fanOut = CreateFanOut(DeadLetter(sink), reporter);
        var cancelling = new TestObserver(
            "cancelling",
            token =>
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled(token);
            }
        );

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            DispatchAsync(
                    fanOut,
                    [cancelling, Observer("later", () => laterCalled = true)],
                    cancellation.Token
                )
                .AsTask()
        );

        laterCalled.ShouldBeFalse();
        reporter.Attempts.ShouldBe(0);
        sink.Attempts.ShouldBe(0);
    }

    [Test]
    public void BoundedRetry_WithOneTotalAttempt_RejectsImplicitRetryDefault() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ObserverFailurePolicy<TestBoundary, TestDeadLetter>.BoundedRetry
            {
                Boundary = _boundary,
                AttemptLimit = 1,
            }
        );

    [Test]
    public void CorrelationId_WithWhitespace_RejectsEmptyContext() =>
        Should.Throw<ArgumentException>(() => ObserverCorrelationId.Named(" "));

    private static ObserverFailurePolicy<TestBoundary, TestDeadLetter> Continue() =>
        new ObserverFailurePolicy<TestBoundary, TestDeadLetter>.ContinueAndReport
        {
            Boundary = _boundary,
        };

    private static ObserverFailurePolicy<TestBoundary, TestDeadLetter> Retry(int attemptLimit) =>
        new ObserverFailurePolicy<TestBoundary, TestDeadLetter>.BoundedRetry
        {
            Boundary = _boundary,
            AttemptLimit = attemptLimit,
        };

    private static ObserverFailurePolicy<TestBoundary, TestDeadLetter> DeadLetter(
        RecordingDeadLetterSink sink
    ) =>
        new ObserverFailurePolicy<TestBoundary, TestDeadLetter>.DeadLetter
        {
            Boundary = _boundary,
            Sink = sink,
        };

    private static ObserverFanOut<TestBoundary, TestEvent, TestDeadLetter> CreateFanOut(
        ObserverFailurePolicy<TestBoundary, TestDeadLetter> policy,
        RecordingReporter reporter
    ) => new(policy, reporter, new FixedCorrelationIdProvider());

    private static ValueTask<ObserverFanOutOutcome> DispatchAsync(
        ObserverFanOut<TestBoundary, TestEvent, TestDeadLetter> fanOut,
        IReadOnlyList<TestObserver> observers,
        CancellationToken cancellationToken
    ) =>
        fanOut.DispatchAsync(
            observers,
            _ => new ObserverDispatch<TestEvent, TestDeadLetter>
            {
                Event = new TestEvent("private chat text", "raw oauth payload"),
                EventIdentity = _eventIdentity,
                DeadLetter = new TestDeadLetter("event-42"),
            },
            observer => ObserverIdentity.Named(observer.Name),
            static (observer, _, token) => observer.InvokeAsync(token),
            cancellationToken
        );

    private static TestObserver Observer(string name, Action operation) =>
        new(
            name,
            _ =>
            {
                operation();
                return ValueTask.CompletedTask;
            }
        );

    private static TestObserver FailingObserver(
        string name,
        Exception exception,
        List<string> order
    ) =>
        new(
            name,
            _ =>
            {
                order.Add(name);
                return ValueTask.FromException(exception);
            }
        );

    private static void AssertFailure(
        ObserverFailureSummary summary,
        string observer,
        int attempt,
        ObserverFailureClassification classification,
        Type failureType
    )
    {
        summary.Boundary.ShouldBe(_boundary);
        summary.Event.ShouldBe(_eventIdentity);
        summary.Observer.ShouldBe(ObserverIdentity.Named(observer));
        summary.CorrelationId.ShouldBe(_correlationId);
        summary.Attempt.ShouldBe(attempt);
        summary.Classification.ShouldBe(classification);
        summary.FailureType.ShouldBe(failureType.FullName);
    }

    private sealed class TestBoundary;

    private sealed record TestEvent(string Message, string RawPayload);

    private sealed record TestDeadLetter(string EventId) : IObserverDeadLetterPayload;

    private sealed class TestObserver(string name, Func<CancellationToken, ValueTask> invoke)
    {
        internal string Name { get; } = name;

        internal ValueTask InvokeAsync(CancellationToken cancellationToken) =>
            invoke(cancellationToken);
    }

    private sealed class RecordingReporter : IObserverFailureDiagnosticReporter
    {
        private readonly Queue<Exception> _failures = [];

        internal int Attempts { get; private set; }

        internal List<ObserverFailureDiagnosticReport> Reports { get; } = [];

        public ValueTask ReportAsync(
            ObserverFailureDiagnosticReport report,
            CancellationToken cancellationToken
        )
        {
            Attempts++;
            if (_failures.TryDequeue(out var failure))
            {
                return ValueTask.FromException(failure);
            }

            Reports.Add(report);
            return ValueTask.CompletedTask;
        }

        internal void EnqueueFailure(Exception failure) => _failures.Enqueue(failure);
    }

    private sealed class RecordingDeadLetterSink
        : IDurableObserverDeadLetterSink<TestBoundary, TestDeadLetter>
    {
        private readonly Queue<Exception> _failures = [];

        internal int Attempts { get; private set; }

        internal List<ObserverDeadLetter<TestDeadLetter>> Entries { get; } = [];

        public ValueTask StoreAsync(
            ObserverDeadLetter<TestDeadLetter> deadLetter,
            CancellationToken cancellationToken
        )
        {
            Attempts++;
            if (_failures.TryDequeue(out var failure))
            {
                return ValueTask.FromException(failure);
            }

            Entries.Add(deadLetter);
            return ValueTask.CompletedTask;
        }

        internal void EnqueueFailure(Exception failure) => _failures.Enqueue(failure);
    }

    private sealed class FixedCorrelationIdProvider : IObserverCorrelationIdProvider
    {
        public ObserverCorrelationId Next() => _correlationId;
    }
}
