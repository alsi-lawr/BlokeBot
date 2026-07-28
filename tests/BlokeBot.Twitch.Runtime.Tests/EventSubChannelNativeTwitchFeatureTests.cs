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
}
