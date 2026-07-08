using BlokeBot.AppEvents;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class AppEventBusTests
{
    [Test]
    public async Task Publish_continues_notifying_subscribers_after_failure()
    {
        var events = new AppEventBus();
        var received = new List<string>();

        events.Subscribe(
            AppEventKind.PointsChanged,
            _ =>
            {
                received.Add("first");
                return Task.CompletedTask;
            }
        );
        events.Subscribe(
            AppEventKind.PointsChanged,
            _ => Task.FromException(new InvalidOperationException("subscriber failed"))
        );
        events.Subscribe(
            AppEventKind.PointsChanged,
            _ =>
            {
                received.Add("third");
                return Task.CompletedTask;
            }
        );

        await events.PublishAsync(AppEventKind.PointsChanged);

        received.ShouldBe(["first", "third"]);
    }
}
