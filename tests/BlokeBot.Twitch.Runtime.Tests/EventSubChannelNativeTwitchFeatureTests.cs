using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelNativeTwitchFeatureTests : EventSubChannelRecoveryTestBase
{
    [Test]
    public async Task NativeTwitchDisable_UnresolvedCleanupRetainsChatAndRetriesBeforeReenable()
    {
        var operations = new ScriptedChannelOperations();
        operations.SetNativeTwitchEnabled("channel", true);
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.CreateCount("channel").ShouldBe(2);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);

        operations.SetNativeTwitchEnabled("channel", false);
        operations.EnqueueDeleteFailure(
            "channel",
            new InvalidOperationException("remote operation cleanup unavailable")
        );
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(1);
        operations
            .DeleteAttempts("channel")
            .ShouldHaveSingleItem()
            .Authorization.ShouldBeOfType<EventSubAuthorizationContext.ConfiguredBotOperations>();
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        operations.CompleteStopCount("channel").ShouldBe(0);

        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(2);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        operations.CompleteStopCount("channel").ShouldBe(0);

        operations.SetNativeTwitchEnabled("channel", true);
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.CreateCount("channel").ShouldBe(3);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NativeTwitchGroups_CleanupAndRetryIndependentlyWhileChatRemainsActive(
        int failingGroupIndex
    )
    {
        var operations = new ScriptedChannelOperations();
        operations.SetNativeTwitchEnabled("channel", true);
        QueueBroadcasterAccounts(operations);
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        operations.CreateCount("channel").ShouldBe(5);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);

        operations.SetNativeTwitchEnabled("channel", false);
        for (var index = 0; index < failingGroupIndex; index++)
        {
            operations.EnqueueDeleteSuccess("channel");
        }
        operations.EnqueueDeleteFailure(
            "channel",
            new InvalidOperationException("selected Native group cleanup unavailable")
        );

        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(failingGroupIndex + 1);
        AssertAuthorization(
            operations.DeleteAttempts("channel")[^1].Authorization,
            failingGroupIndex
        );
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        operations.CompleteStopCount("channel").ShouldBe(0);

        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(5);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        operations.CompleteStopCount("channel").ShouldBe(0);

        QueueBroadcasterAccounts(operations);
        operations.SetNativeTwitchEnabled("channel", true);
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.CreateCount("channel").ShouldBe(9);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
    }

    private static void QueueBroadcasterAccounts(ScriptedChannelOperations operations)
    {
        operations.EnqueueBroadcasterAccountResult("channel", "channel");
        operations.EnqueueBroadcasterAccountResult("channel", "channel");
        operations.EnqueueBroadcasterAccountResult("channel", "channel");
    }

    private static void AssertAuthorization(
        EventSubAuthorizationContext authorization,
        int groupIndex
    )
    {
        switch (groupIndex)
        {
            case 0:
                authorization.ShouldBeOfType<EventSubAuthorizationContext.ConfiguredBotOperations>();
                break;
            case 1:
                authorization
                    .ShouldBeOfType<EventSubAuthorizationContext.Broadcaster>()
                    .Operation.ShouldBe(EventSubBroadcasterOperationKind.Polls);
                break;
            case 2:
                authorization
                    .ShouldBeOfType<EventSubAuthorizationContext.Broadcaster>()
                    .Operation.ShouldBe(EventSubBroadcasterOperationKind.RewardRedemptions);
                break;
            case 3:
                authorization
                    .ShouldBeOfType<EventSubAuthorizationContext.Broadcaster>()
                    .Operation.ShouldBe(EventSubBroadcasterOperationKind.Predictions);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(groupIndex));
        }
    }
}
