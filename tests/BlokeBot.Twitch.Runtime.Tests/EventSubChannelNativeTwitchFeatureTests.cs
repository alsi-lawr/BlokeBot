using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelNativeTwitchFeatureTests : EventSubChannelRecoveryTestBase
{
    [Test]
    public async Task RaidCleanupFailure_DisableRetriesBeforeReenableAndRetainsChat()
    {
        var operations = new ScriptedChannelOperations();
        operations.SetNativeTwitchEnabled("channel", true);
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.CreateCount("channel").ShouldBe(3);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);

        operations.SetNativeTwitchEnabled("channel", false);
        operations.EnqueueDeleteSuccess("channel");
        operations.EnqueueDeleteFailure(
            "channel",
            new InvalidOperationException("remote operation cleanup unavailable")
        );
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(2);
        operations
            .DeleteAttempts("channel")
            .Last()
            .Authorization.ShouldBeOfType<EventSubAuthorizationContext.ConfiguredBot>();
        operations
            .OperationKinds("channel")
            .Take(3)
            .ShouldBe([
                null,
                EventSubOperationSubscriptionKind.Shoutouts,
                EventSubOperationSubscriptionKind.Raids,
            ]);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        operations.CompleteStopCount("channel").ShouldBe(0);

        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(3);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        operations.CompleteStopCount("channel").ShouldBe(0);

        operations.SetNativeTwitchEnabled("channel", true);
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.CreateCount("channel").ShouldBe(5);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
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

        operations.CreateCount("channel").ShouldBe(6);
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

        operations.DeleteCount("channel").ShouldBe(6);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        operations.CompleteStopCount("channel").ShouldBe(0);

        QueueBroadcasterAccounts(operations);
        operations.SetNativeTwitchEnabled("channel", true);
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.CreateCount("channel").ShouldBe(11);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
    }

    [Test]
    public async Task CompleteNativeSet_ChannelRemovalDeletesEveryGroupAndChat()
    {
        var operations = new ScriptedChannelOperations();
        operations.SetNativeTwitchEnabled("channel", true);
        QueueBroadcasterAccounts(operations);
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(6);
        var attempts = operations.DeleteAttempts("channel");
        for (var groupIndex = 0; groupIndex < 5; groupIndex++)
        {
            AssertAuthorization(attempts[groupIndex].Authorization, groupIndex);
        }
        attempts[^1].Authorization.ShouldBeOfType<EventSubAuthorizationContext.ConfiguredBot>();
        operations.CompleteStopCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBeEmpty();
    }

    [Test]
    public async Task RaidRevocation_ReconnectRecreatesCompleteNativeSetIncludingRaid()
    {
        var operations = new ScriptedChannelOperations();
        operations.SetNativeTwitchEnabled("channel", true);
        QueueBroadcasterAccounts(operations);
        await using (var initial = CreateHarness(operations, attemptLimit: 1))
        {
            initial.Session.Start(["channel"], CancellationToken.None);
            await initial.Session.DrainAsync();
        }

        EventSubSessionFailureClassifier
            .Classify(new EventSubSubscriptionRevokedException(), CancellationToken.None)
            .ShouldBe(RuntimeSessionFailureClassification.Transient);

        QueueBroadcasterAccounts(operations);
        await using (var replacement = CreateHarness(operations, attemptLimit: 1))
        {
            replacement.Session.Start(["channel"], CancellationToken.None);
            await replacement.Session.DrainAsync();
        }

        operations.CreateCount("channel").ShouldBe(12);
        var replacementAuthorizations = operations.Authorizations("channel").TakeLast(6).ToArray();
        replacementAuthorizations[0].ShouldBeOfType<EventSubAuthorizationContext.ConfiguredBot>();
        for (var groupIndex = 0; groupIndex < 5; groupIndex++)
        {
            AssertAuthorization(replacementAuthorizations[groupIndex + 1], groupIndex);
        }
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
                authorization.ShouldBeOfType<EventSubAuthorizationContext.ConfiguredBot>();
                break;
            case 2:
                authorization
                    .ShouldBeOfType<EventSubAuthorizationContext.Broadcaster>()
                    .Operation.ShouldBe(EventSubBroadcasterOperationKind.Polls);
                break;
            case 3:
                authorization
                    .ShouldBeOfType<EventSubAuthorizationContext.Broadcaster>()
                    .Operation.ShouldBe(EventSubBroadcasterOperationKind.RewardRedemptions);
                break;
            case 4:
                authorization
                    .ShouldBeOfType<EventSubAuthorizationContext.Broadcaster>()
                    .Operation.ShouldBe(EventSubBroadcasterOperationKind.Predictions);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(groupIndex));
        }
    }
}
