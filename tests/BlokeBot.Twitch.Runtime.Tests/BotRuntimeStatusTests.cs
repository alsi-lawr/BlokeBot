using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class BotRuntimeStatusTests
{
    [Test]
    public void LifecycleTransitions_UpdatingStatus_PreserveValidCases()
    {
        var status = new BotRuntimeStatusStore();

        _ = status.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();

        status.MarkAuthorized();

        _ = status.Current.ShouldBeOfType<BotRuntimeStatus.Authorized>();

        status.MarkConnected(["channel"]);

        status.Current.ShouldBeOfType<BotRuntimeStatus.Connected>().Channels.ShouldBe(["channel"]);

        status.MarkDisconnected();

        _ = status.Current.ShouldBeOfType<BotRuntimeStatus.Authorized>();

        status.MarkUnauthorized();

        _ = status.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();
    }

    [Test]
    public void EmptyChannelSet_MarkingConnected_RejectsContradictoryStatus()
    {
        var status = new BotRuntimeStatusStore();

        _ = Should.Throw<ArgumentException>(() => status.MarkConnected([]));

        _ = status.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();
    }
}
