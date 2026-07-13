using System.Reflection;
using BlokeBot.Eventing;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Eventing.Tests;

public sealed class ObserverFanOutTests
{
    private static readonly ObserverBoundary _boundary =
        ObserverBoundary.Named("Test.ObserverFanOut");
    private static readonly ObserverEventIdentity _eventIdentity =
        ObserverEventIdentity.Named("TestEvent");
    private static readonly ObserverCorrelationId _correlationId =
        ObserverCorrelationId.Named("correlation-123");

    [Test]
    public async Task ContinueAndReport_FailingObserver_ContinuesInOrderWithRedactedOutcome()
    {
        var failure = new InvalidOperationException("secret exception payload");
        var order = new List<string>();
        var logger = new RecordingLogger();
        var observers = new[]
        {
            Observer("first", () => order.Add("first")),
            FailingObserver("failing", failure, order),
            Observer("third", () => order.Add("third")),
        };
        var fanOut = CreateFanOut(Continue(), logger);

        var outcome = await DispatchAsync(fanOut, observers, CancellationToken.None);

        order.ShouldBe(["first", "failing", "third"]);
        var handled = outcome.ShouldBeOfType<
            ObserverFanOutOutcome.CompletedWithFailures
        >();
        var summary = handled.Failures.ShouldHaveSingleItem();
        AssertFailure(
            summary,
            "failing",
            attempt: 1,
            ObserverFailureClassification.Terminal,
            typeof(InvalidOperationException)
        );
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(failure.Message);
        entry.Properties["FailureType"].ShouldBe(typeof(InvalidOperationException).FullName);
        handled.ToString().ShouldNotContain("secret exception payload");
        typeof(ObserverFailureSummary)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ShouldNotContain(name =>
                name.Contains("Exception", StringComparison.Ordinal)
                || name.Contains("Message", StringComparison.Ordinal)
                || name.Contains("Payload", StringComparison.Ordinal)
            );
    }

    [Test]
    public async Task BoundedRetry_TransientThenSuccess_RetriesOnlyFailedObserver()
    {
        var transient = new IOException("temporary observer failure");
        var order = new List<string>();
        var retryAttempt = 0;
        var logger = new RecordingLogger();
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
        var fanOut = CreateFanOut(Retry(attemptLimit: 3), logger);

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
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(transient.Message);
        entry.Properties["FailureType"].ShouldBe(typeof(IOException).FullName);
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
        var logger = new RecordingLogger();
        var observers = new[]
        {
            new TestObserver(
                "exhausting",
                _ => ValueTask.FromException(failures[attempts++])
            ),
            Observer("later", () => laterCalled = true),
        };
        var fanOut = CreateFanOut(Retry(attemptLimit: 3), logger);

        var outcome = await DispatchAsync(fanOut, observers, CancellationToken.None);

        attempts.ShouldBe(3);
        laterCalled.ShouldBeTrue();
        var handled = outcome.ShouldBeOfType<
            ObserverFanOutOutcome.CompletedWithFailures
        >();
        handled.Failures.Select(failure => failure.Attempt).ShouldBe([1, 2, 3]);
        handled.Failures.ShouldAllBe(failure =>
            failure.Classification == ObserverFailureClassification.Transient
        );
        logger.Entries.Count.ShouldBe(failures.Length);
        foreach (var entry in logger.Entries)
        {
            entry.Exception.ShouldBeNull();
            entry.Properties["FailureType"].ShouldBe(typeof(IOException).FullName);
        }
    }

    [Test]
    public async Task BoundedRetry_TerminalFailure_DoesNotRetry()
    {
        var attempts = 0;
        var logger = new RecordingLogger();
        var fanOut = CreateFanOut(Retry(attemptLimit: 4), logger);
        var observer = new TestObserver(
            "terminal",
            _ =>
            {
                attempts++;
                return ValueTask.FromException(
                    new InvalidOperationException("terminal")
                );
            }
        );

        var outcome = await DispatchAsync(fanOut, [observer], CancellationToken.None);

        attempts.ShouldBe(1);
        outcome.ShouldBeOfType<ObserverFanOutOutcome.CompletedWithFailures>()
            .Failures.ShouldHaveSingleItem()
            .Classification.ShouldBe(ObserverFailureClassification.Terminal);
    }

    [Test]
    public async Task DeadLetter_FailingObserver_StoresOneTypedRedactedRecord()
    {
        var observerFailure = new InvalidOperationException("private failure message");
        var logger = new RecordingLogger();
        var sink = new RecordingDeadLetterSink();
        var fanOut = CreateFanOut(DeadLetter(sink), logger);

        var outcome = await DispatchAsync(
            fanOut,
            [FailingObserver("dead-lettered", observerFailure, [])],
            CancellationToken.None
        );

        outcome.ShouldBeOfType<ObserverFanOutOutcome.CompletedWithFailures>();
        sink.Attempts.ShouldBe(1);
        var deadLetter = sink.Entries.ShouldHaveSingleItem();
        deadLetter.Payload.ShouldBe(new TestDeadLetter("event-42"));
        deadLetter.Failure.Observer.ShouldBe(ObserverIdentity.Named("dead-lettered"));
        deadLetter.ToString().ShouldNotContain("private failure message");
        typeof(TestDeadLetter)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ShouldBe(["EventId"]);
        typeof(ObserverDeadLetter<TestDeadLetter>)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order()
            .ShouldBe(["Failure", "Payload"]);
    }

    [Test]
    public async Task LoggingFailure_AfterObserverFailure_ContinuesSiblingThenEscalatesOnce()
    {
        var observerFailure = new InvalidOperationException("observer failed");
        var loggerFailure = new IOException("logger failed");
        var laterCalled = false;
        var logger = new RecordingLogger();
        logger.EnqueueFailure(loggerFailure);
        var fanOut = CreateFanOut(Continue(), logger);

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
        logger.Attempts.ShouldBe(1);
        exception.Causes.ShouldBe([observerFailure, loggerFailure]);
        exception.Failures.ShouldHaveSingleItem().Observer.ShouldBe(
            ObserverIdentity.Named("failing")
        );
        var handlingFailure = exception.HandlingFailures.ShouldHaveSingleItem();
        handlingFailure.Stage.ShouldBe(ObserverFailureHandlingStage.Logging);
        handlingFailure.FailureType.ShouldBe(typeof(IOException).FullName);
        exception.InnerException.ShouldBeNull();
    }

    [Test]
    public async Task LoggingAndDeadLetterFailures_ContinueSiblingAndRetainEveryExactFailure()
    {
        var observerFailure = new InvalidOperationException("observer failed");
        var loggerFailure = new IOException("logger failed");
        var sinkFailure = new IOException("sink failed");
        var laterCalled = false;
        var logger = new RecordingLogger();
        logger.EnqueueFailure(loggerFailure);
        var sink = new RecordingDeadLetterSink();
        sink.EnqueueFailure(sinkFailure);
        var fanOut = CreateFanOut(DeadLetter(sink), logger);

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
        logger.Attempts.ShouldBe(1);
        sink.Attempts.ShouldBe(1);
        exception.Causes.ShouldBe([observerFailure, loggerFailure, sinkFailure]);
        exception.Failures.ShouldHaveSingleItem();
        exception.HandlingFailures.Select(failure => failure.Stage)
            .ShouldBe([
                ObserverFailureHandlingStage.Logging,
                ObserverFailureHandlingStage.DeadLetterSink,
            ]);
    }

    [Test]
    public async Task DispatchCancellation_FromObserver_PropagatesWithoutReportOrDeadLetter()
    {
        using var cancellation = new CancellationTokenSource();
        var laterCalled = false;
        var logger = new RecordingLogger();
        var sink = new RecordingDeadLetterSink();
        var fanOut = CreateFanOut(DeadLetter(sink), logger);
        var cancelling = new TestObserver(
            "cancelling",
            token =>
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled(token);
            }
        );

        await Should.ThrowAsync<OperationCanceledException>(() =>
            DispatchAsync(
                    fanOut,
                    [cancelling, Observer("later", () => laterCalled = true)],
                    cancellation.Token
                )
                .AsTask()
        );

        laterCalled.ShouldBeFalse();
        logger.Attempts.ShouldBe(0);
        sink.Attempts.ShouldBe(0);
    }

    [Test]
    public void BoundedRetry_WithOneTotalAttempt_RejectsImplicitRetryDefault()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ObserverFailurePolicy<TestBoundary, TestDeadLetter>.BoundedRetry
            {
                Boundary = _boundary,
                AttemptLimit = 1,
            }
        );
    }

    [Test]
    public void CorrelationId_WithWhitespace_RejectsEmptyContext()
    {
        Should.Throw<ArgumentException>(() => ObserverCorrelationId.Named(" "));
    }

    private static ObserverFailurePolicy<TestBoundary, TestDeadLetter> Continue()
    {
        return new ObserverFailurePolicy<TestBoundary, TestDeadLetter>.ContinueAndReport
        {
            Boundary = _boundary,
        };
    }

    private static ObserverFailurePolicy<TestBoundary, TestDeadLetter> Retry(
        int attemptLimit
    )
    {
        return new ObserverFailurePolicy<TestBoundary, TestDeadLetter>.BoundedRetry
        {
            Boundary = _boundary,
            AttemptLimit = attemptLimit,
        };
    }

    private static ObserverFailurePolicy<TestBoundary, TestDeadLetter> DeadLetter(
        RecordingDeadLetterSink sink
    )
    {
        return new ObserverFailurePolicy<TestBoundary, TestDeadLetter>.DeadLetter
        {
            Boundary = _boundary,
            Sink = sink,
        };
    }

    private static ObserverFanOut<TestBoundary, TestEvent, TestDeadLetter> CreateFanOut(
        ObserverFailurePolicy<TestBoundary, TestDeadLetter> policy,
        RecordingLogger logger
    )
    {
        return new(policy, logger, new FixedCorrelationIdProvider());
    }

    private static ValueTask<ObserverFanOutOutcome> DispatchAsync(
        ObserverFanOut<TestBoundary, TestEvent, TestDeadLetter> fanOut,
        IReadOnlyList<TestObserver> observers,
        CancellationToken cancellationToken
    )
    {
        return fanOut.DispatchAsync(
            observers,
            _ =>
                new ObserverDispatch<TestEvent, TestDeadLetter>
                {
                    Event = new TestEvent("private chat text", "raw oauth payload"),
                    EventIdentity = _eventIdentity,
                    DeadLetter = new TestDeadLetter("event-42"),
                },
            observer => ObserverIdentity.Named(observer.Name),
            static (observer, _, token) => observer.InvokeAsync(token),
            cancellationToken
        );
    }

    private static TestObserver Observer(string name, Action operation)
    {
        return new(
            name,
            _ =>
            {
                operation();
                return ValueTask.CompletedTask;
            }
        );
    }

    private static TestObserver FailingObserver(
        string name,
        Exception exception,
        List<string> order
    )
    {
        return new(
            name,
            _ =>
            {
                order.Add(name);
                return ValueTask.FromException(exception);
            }
        );
    }

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

    private sealed class TestObserver(
        string name,
        Func<CancellationToken, ValueTask> invoke
    )
    {
        internal string Name { get; } = name;

        internal ValueTask InvokeAsync(CancellationToken cancellationToken)
        {
            return invoke(cancellationToken);
        }
    }

    private sealed class RecordingLogger
        : ILogger<ObserverFanOut<TestBoundary, TestEvent, TestDeadLetter>>
    {
        private readonly Queue<Exception> _failures = [];

        internal int Attempts { get; private set; }

        internal List<LogEntry> Entries { get; } = [];

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
            Attempts++;
            if (_failures.TryDequeue(out var failure))
            {
                throw failure;
            }

            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(
                new LogEntry(
                    logLevel,
                    formatter(state, exception),
                    exception,
                    properties
                )
            );
        }

        internal void EnqueueFailure(Exception failure)
        {
            _failures.Enqueue(failure);
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties
    );

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose() { }
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

        internal void EnqueueFailure(Exception failure)
        {
            _failures.Enqueue(failure);
        }
    }

    private sealed class FixedCorrelationIdProvider : IObserverCorrelationIdProvider
    {
        public ObserverCorrelationId Next()
        {
            return _correlationId;
        }
    }
}
