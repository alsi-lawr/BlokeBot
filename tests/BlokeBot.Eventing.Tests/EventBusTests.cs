using BlokeBot.Eventing;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Eventing.Tests;

public sealed class EventBusTests
{
    [Test]
    public async Task FailingSubscriber_PublishingEvent_NotifiesRemainingSubscribers()
    {
        var events = new EventBus<string>();
        var received = new List<string>();

        events.Subscribe(
            "changed",
            _ =>
            {
                received.Add("first");
                return Task.CompletedTask;
            }
        );
        events.Subscribe(
            "changed",
            _ => Task.FromException(new InvalidOperationException("subscriber failed"))
        );
        events.Subscribe(
            "changed",
            _ =>
            {
                received.Add("third");
                return Task.CompletedTask;
            }
        );

        await events.PublishAsync("changed");

        received.ShouldBe(["first", "third"]);
    }

    [Test]
    public async Task DisposedSubscription_PublishingEvent_DoesNotNotifyHandler()
    {
        var events = new EventBus<string>();
        var received = 0;
        var subscription = events.Subscribe(
            "changed",
            _ =>
            {
                received++;
                return Task.CompletedTask;
            }
        );

        await events.PublishAsync("changed");
        subscription.Dispose();
        await events.PublishAsync("changed");

        received.ShouldBe(1);
    }

    [Test]
    public async Task SubscriptionSet_Disposing_UnsubscribesEveryHandler()
    {
        var events = new EventBus<string>();
        var received = 0;
        using var subscriptions = new EventSubscriptionSet([
            events.Subscribe(
                "changed",
                _ =>
                {
                    received++;
                    return Task.CompletedTask;
                }
            ),
            events.Subscribe(
                "changed",
                _ =>
                {
                    received++;
                    return Task.CompletedTask;
                }
            ),
        ]);

        subscriptions.Dispose();
        await events.PublishAsync("changed");

        received.ShouldBe(0);
    }

    [Test]
    public async Task MultipleSubscribedKeys_PublishingEvents_NotifiesUntilDisposed()
    {
        var events = new EventBus<string>();
        var received = new List<string>();
        var subscription = events.Subscribe(
            ["first", "second"],
            notification =>
            {
                received.Add(notification.Key);
                return Task.CompletedTask;
            }
        );

        await events.PublishAsync("first");
        await events.PublishAsync("second");
        await events.PublishAsync("third");
        subscription.Dispose();
        await events.PublishAsync("first");

        received.ShouldBe(["first", "second"]);
    }

}
