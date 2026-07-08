using BlokeBot.Eventing;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Eventing.Tests;

public sealed class EventBusTests
{
    [Test]
    public async Task Publish_continues_notifying_subscribers_after_failure()
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
    public async Task Disposed_subscription_no_longer_receives_events()
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
    public async Task Subscription_set_disposes_all_subscriptions()
    {
        var events = new EventBus<string>();
        var received = 0;
        using var subscriptions = new EventSubscriptionSet(
            [
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
            ]
        );

        subscriptions.Dispose();
        await events.PublishAsync("changed");

        received.ShouldBe(0);
    }

    [Test]
    public async Task Multi_key_subscription_receives_each_key_until_disposed()
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

    [Test]
    public async Task Event_notifier_publishes_configured_key()
    {
        var events = new EventBus<string>();
        var received = new List<string>();
        events.Subscribe(
            "changed",
            notification =>
            {
                received.Add(notification.Key);
                return Task.CompletedTask;
            }
        );
        var notifier = new StringEventNotifier(events, "changed");

        await notifier.NotifyChangedAsync();

        received.ShouldBe(["changed"]);
    }

    private sealed class StringEventNotifier(EventBus<string> events, string key)
        : EventNotifier<string>(events, key);
}
