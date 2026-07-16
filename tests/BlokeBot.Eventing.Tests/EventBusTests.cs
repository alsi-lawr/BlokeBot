using BlokeBot.Eventing;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Eventing.Tests;

public sealed class EventBusTests
{
    [Test]
    public async Task FailingSubscriber_PublishingEvent_ReturnsFailuresAndNotifiesRemainingSubscribers()
    {
        var failure = new InvalidOperationException("subscriber failed");
        var reporter = new RecordingReporter();
        var events = CreateBus(reporter);
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
        correlations
            .Distinct()
            .ShouldHaveSingleItem()
            .ShouldBe(ObserverCorrelationId.Named("event-correlation"));
        var handled = outcome.ShouldBeOfType<ObserverFanOutOutcome.CompletedWithFailures>();
        var summary = handled.Failures.ShouldHaveSingleItem();
        summary.Boundary.ShouldBe(ObserverBoundary.Named("Test.EventBus"));
        summary.Event.ShouldBe(ObserverEventIdentity.Named("Event.changed"));
        summary.Observer.ShouldBe(ObserverIdentity.Named("failing"));
        summary.CorrelationId.ShouldBe(ObserverCorrelationId.Named("event-correlation"));
        summary.Attempt.ShouldBe(1);
        summary.Classification.ShouldBe(ObserverFailureClassification.Terminal);
        reporter.Reports.ShouldHaveSingleItem().Exception.ShouldBeSameAs(failure);
    }

    [Test]
    public async Task DisposedSubscription_PublishingEvent_DoesNotNotifyHandler()
    {
        var events = CreateBus(new RecordingReporter());
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
        var events = CreateBus(new RecordingReporter());
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
        var events = CreateBus(new RecordingReporter());
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

    private static EventBus<string> CreateBus(RecordingReporter reporter)
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
            reporter,
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

    private sealed class RecordingReporter : IObserverFailureDiagnosticReporter
    {
        internal List<ObserverFailureDiagnosticReport> Reports { get; } = [];

        public ValueTask ReportAsync(
            ObserverFailureDiagnosticReport report,
            CancellationToken cancellationToken
        )
        {
            Reports.Add(report);
            return ValueTask.CompletedTask;
        }
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
