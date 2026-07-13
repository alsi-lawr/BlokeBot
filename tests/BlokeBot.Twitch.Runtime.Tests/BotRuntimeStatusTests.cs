using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class BotRuntimeStatusTests
{
    [Test]
    public void LifecycleTransitions_UpdatingStatus_PreserveValidCases()
    {
        var status = new BotRuntimeStatusStore();

        status.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();

        status.MarkAuthorized();

        status.Current.ShouldBeOfType<BotRuntimeStatus.Authorized>();

        status.MarkConnected(["channel"]);

        status.Current.ShouldBeOfType<BotRuntimeStatus.Connected>().Channels.ShouldBe(["channel"]);

        status.MarkDisconnected();

        status.Current.ShouldBeOfType<BotRuntimeStatus.Authorized>();

        status.MarkUnauthorized();

        status.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();
    }

    [Test]
    public void EmptyChannelSet_MarkingConnected_RejectsContradictoryStatus()
    {
        var status = new BotRuntimeStatusStore();

        Should.Throw<ArgumentException>(() => status.MarkConnected([]));

        status.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();
    }
}
