using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelExactRequirementTests : EventSubChannelRecoveryTestBase
{
    private static readonly EventSubExactSubscription _channelBan = new("channel.ban", "1");

    [Test]
    public async Task ExactRequirement_MustBeProvisionedBeforeChannelPublishesHealthy()
    {
        var failure = new InvalidOperationException("exact subscription rejected");
        var operations = new ScriptedChannelOperations();
        operations.SetExactRequirements("channel", _channelBan);
        operations.EnqueueExactCreateFailure("channel", failure);
        await using var harness = CreateHarness(operations, attemptLimit: 1);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        _ = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        operations.StartupDeliveryCount("channel").ShouldBe(0);
        operations.ExactCreations("channel").ShouldBe([_channelBan]);

        await ReconcileAsync(harness.Session, ["channel"], EventSubChannelRecoveryTrigger.Explicit);

        _ = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
        operations.StartupDeliveryCount("channel").ShouldBe(1);
        operations.ExactCreations("channel").ShouldBe([_channelBan, _channelBan]);
    }

    [Test]
    public async Task ExactAndNativeRequirements_ReprovisionTogetherAfterRevocationAndReconnect()
    {
        var operations = new ScriptedChannelOperations();
        operations.SetNativeTwitchFeatureEnabled(
            "channel",
            EventSubOperationSubscriptionKind.Raids,
            enabled: true
        );
        operations.SetExactRequirements("channel", _channelBan);

        await using (var initial = CreateHarness(operations, attemptLimit: 1))
        {
            initial.Session.Start(["channel"], CancellationToken.None);
            await initial.Session.DrainAsync();

            await initial.Session.RepairRevokedSubscriptionAndDrainAsync(
                operations.ExactSubscriptionIds("channel").ShouldHaveSingleItem(),
                _ => ValueTask.FromResult<IReadOnlyList<string>>(["channel"]),
                CancellationToken.None
            );

            _ = initial
                .Status.Current.Channels.ShouldHaveSingleItem()
                .ShouldBeOfType<EventSubChannelStatus.Healthy>();
        }

        operations.ExactCreations("channel").ShouldBe([_channelBan, _channelBan]);
        operations
            .OperationKinds("channel")
            .Count(kind => kind is EventSubOperationSubscriptionKind.Raids)
            .ShouldBe(2);

        await using (var reconnect = CreateHarness(operations, attemptLimit: 1))
        {
            reconnect.Session.Start(["channel"], CancellationToken.None);
            await reconnect.Session.DrainAsync();

            _ = reconnect
                .Status.Current.Channels.ShouldHaveSingleItem()
                .ShouldBeOfType<EventSubChannelStatus.Healthy>();
        }

        operations.ExactCreations("channel").ShouldBe([_channelBan, _channelBan, _channelBan]);
        operations
            .OperationKinds("channel")
            .Count(kind => kind is EventSubOperationSubscriptionKind.Raids)
            .ShouldBe(3);
    }

    [Test]
    public async Task ExactRequirementRemoval_DeletesOnlyItsCentralSubscription()
    {
        var operations = new ScriptedChannelOperations();
        operations.SetNativeTwitchFeatureEnabled(
            "channel",
            EventSubOperationSubscriptionKind.Raids,
            enabled: true
        );
        operations.SetExactRequirements("channel", _channelBan);
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        var exactId = operations.ExactSubscriptionIds("channel").ShouldHaveSingleItem();

        operations.SetExactRequirements("channel");
        await ReconcileAsync(harness.Session, ["channel"], EventSubChannelRecoveryTrigger.Explicit);

        operations
            .DeleteAttempts("channel")
            .ShouldHaveSingleItem()
            .SubscriptionId.ShouldBe(exactId);
        operations.ExactCreations("channel").ShouldBe([_channelBan]);
        operations
            .OperationKinds("channel")
            .Count(kind => kind is EventSubOperationSubscriptionKind.Raids)
            .ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        _ = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
    }
}
