using BlokeBot.Eventing;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class TestEventingTests
{
    [Test]
    public async Task DefaultEventBus_FailingObserver_FailsPublishing()
    {
        var events = TestEventBus.Create<string>();
        var subscription = events.Subscribe(
            "changed",
            ObserverIdentity.Named("failing"),
            (_, _) => ValueTask.FromException(new InvalidOperationException("observer failed"))
        );

        var exception = await Should.ThrowAsync<ObserverFanOutEscalationException>(() =>
            events.PublishAsync("changed", CancellationToken.None).AsTask()
        );

        exception
            .Failures.ShouldHaveSingleItem()
            .Observer.ShouldBe(ObserverIdentity.Named("failing"));
        exception
            .HandlingFailures.ShouldHaveSingleItem()
            .Stage.ShouldBe(ObserverFailureHandlingStage.Reporter);
        subscription.Dispose();
    }
}
