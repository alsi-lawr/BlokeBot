using BlokeBot.Eventing;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Eventing.Tests;

public sealed class EventBusTests
{
    [Test]
    public async Task FailingSubscriber_PublishingEvent_ReturnsFailuresAndNotifiesRemainingSubscribers()
    {
        var failure = new InvalidOperationException("subscriber failed");
        var logger = new RecordingLogger();
        var events = CreateBus(logger);
        var received = new List<string>();
        var correlations = new List<ObserverCorrelationId>();

        events.Subscribe(
            "changed",
            ObserverIdentity.Named("first"),
            (notification, _) =>
            {
                received.Add("first");
                correlations.Add(notification.CorrelationId);
                return ValueTask.CompletedTask;
            }
        );
        events.Subscribe(
            "changed",
            ObserverIdentity.Named("failing"),
            (notification, _) =>
            {
                received.Add("failing");
                correlations.Add(notification.CorrelationId);
                return ValueTask.FromException(failure);
            }
        );
        events.Subscribe(
            "changed",
            ObserverIdentity.Named("third"),
            (notification, _) =>
            {
                received.Add("third");
                correlations.Add(notification.CorrelationId);
                return ValueTask.CompletedTask;
            }
        );

        var outcome = await events.PublishAsync("changed", CancellationToken.None);

        received.ShouldBe(["first", "failing", "third"]);
        correlations.Distinct().ShouldHaveSingleItem()
            .ShouldBe(ObserverCorrelationId.Named("event-correlation"));
        var handled = outcome.ShouldBeOfType<
            ObserverFanOutOutcome.CompletedWithFailures
        >();
        var summary = handled.Failures.ShouldHaveSingleItem();
        summary.Boundary.ShouldBe(ObserverBoundary.Named("Test.EventBus"));
        summary.Event.ShouldBe(ObserverEventIdentity.Named("Event.changed"));
        summary.Observer.ShouldBe(ObserverIdentity.Named("failing"));
        summary.CorrelationId.ShouldBe(ObserverCorrelationId.Named("event-correlation"));
        summary.Attempt.ShouldBe(1);
        summary.Classification.ShouldBe(ObserverFailureClassification.Terminal);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain(failure.Message);
        entry.Properties["FailureType"].ShouldBe(typeof(InvalidOperationException).FullName);
    }

    [Test]
    public async Task PublishCancellation_WhileDispatching_PropagatesWithoutReportingOrLaterSubscriber()
    {
        var logger = new RecordingLogger();
        var events = CreateBus(logger);
        var laterCalled = false;
        using var cancellation = new CancellationTokenSource();
        events.Subscribe(
            "changed",
            ObserverIdentity.Named("cancelling"),
            (_, token) =>
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled(token);
            }
        );
        events.Subscribe(
            "changed",
            ObserverIdentity.Named("later"),
            (_, _) =>
            {
                laterCalled = true;
                return ValueTask.CompletedTask;
            }
        );

        await Should.ThrowAsync<OperationCanceledException>(() =>
            events.PublishAsync("changed", cancellation.Token).AsTask()
        );

        laterCalled.ShouldBeFalse();
        logger.Entries.ShouldBeEmpty();
    }

    [Test]
    public async Task DisposedSubscription_PublishingEvent_DoesNotNotifyHandler()
    {
        var events = CreateBus(new RecordingLogger());
        var received = 0;
        var subscription = events.Subscribe(
            "changed",
            ObserverIdentity.Named("subscriber"),
            (_, _) =>
            {
                received++;
                return ValueTask.CompletedTask;
            }
        );

        await events.PublishAsync("changed", CancellationToken.None);
        subscription.Dispose();
        await events.PublishAsync("changed", CancellationToken.None);

        received.ShouldBe(1);
    }

    [Test]
    public async Task SubscriptionSet_Disposing_UnsubscribesEveryHandler()
    {
        var events = CreateBus(new RecordingLogger());
        var received = 0;
        using var subscriptions = new EventSubscriptionSet([
            events.Subscribe(
                "changed",
                ObserverIdentity.Named("first"),
                (_, _) =>
                {
                    received++;
                    return ValueTask.CompletedTask;
                }
            ),
            events.Subscribe(
                "changed",
                ObserverIdentity.Named("second"),
                (_, _) =>
                {
                    received++;
                    return ValueTask.CompletedTask;
                }
            ),
        ]);

        subscriptions.Dispose();
        await events.PublishAsync("changed", CancellationToken.None);

        received.ShouldBe(0);
    }

    [Test]
    public async Task MultipleSubscribedKeys_PublishingEvents_NotifiesUntilDisposed()
    {
        var events = CreateBus(new RecordingLogger());
        var received = new List<string>();
        var subscription = events.Subscribe(
            ["first", "second"],
            ObserverIdentity.Named("multi-key"),
            (notification, _) =>
            {
                received.Add(notification.Key);
                return ValueTask.CompletedTask;
            }
        );

        await events.PublishAsync("first", CancellationToken.None);
        await events.PublishAsync("second", CancellationToken.None);
        await events.PublishAsync("third", CancellationToken.None);
        subscription.Dispose();
        await events.PublishAsync("first", CancellationToken.None);

        received.ShouldBe(["first", "second"]);
    }

    private static EventBus<string> CreateBus(RecordingLogger logger)
    {
        var fanOut = new ObserverFanOut<
            EventBusObserverBoundary<string>,
            EventNotification<string>,
            EventBusDeadLetter
        >(
            new ObserverFailurePolicy<
                EventBusObserverBoundary<string>,
                EventBusDeadLetter
            >.ContinueAndReport
            {
                Boundary = ObserverBoundary.Named("Test.EventBus"),
            },
            logger,
            new FixedCorrelationIdProvider("event-correlation")
        );
        return new EventBus<string>(
            fanOut,
            new EventBusEventIdentity<string>
            {
                Project = key => ObserverEventIdentity.Named($"Event.{key}"),
            }
        );
    }

    private sealed class RecordingLogger
        : ILogger<
            ObserverFanOut<
                EventBusObserverBoundary<string>,
                EventNotification<string>,
                EventBusDeadLetter
            >
        >
    {
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
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            Entries.Add(
                new LogEntry(formatter(state, exception), exception, properties)
            );
        }
    }

    private sealed record LogEntry(
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties
    );

    private sealed class NullScope : IDisposable
    {
        internal static NullScope Instance { get; } = new();

        public void Dispose() { }
    }

    private sealed class FixedCorrelationIdProvider(string correlationId)
        : IObserverCorrelationIdProvider
    {
        public ObserverCorrelationId Next()
        {
            return ObserverCorrelationId.Named(correlationId);
        }
    }
}
