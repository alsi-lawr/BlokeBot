using System.Threading.Channels;
using BlokeBot.Twitch.Auth;
using Polly.Timeout;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelStartupTests : EventSubChannelRecoveryTestBase
{
    [Test]
    public async Task FreshReconnectSession_DeliversOnceAndDuplicateReconciliationDoesNotResend()
    {
        var operations = new ScriptedChannelOperations();
        await using (var initial = CreateHarness(operations, attemptLimit: 1))
        {
            initial.Session.Start(["channel"], CancellationToken.None);
            await initial.Session.DrainAsync();
            initial.Session.TriggerReconciliation(
                ["channel"],
                EventSubChannelRecoveryTrigger.Explicit
            );
            await initial.Session.DrainAsync();
        }

        operations.StartupDeliveryCount("channel").ShouldBe(1);
        await using (var reconnect = CreateHarness(operations, attemptLimit: 1))
        {
            reconnect.Session.Start(["channel"], CancellationToken.None);
            await reconnect.Session.DrainAsync();
        }

        operations.StartupDeliveryCount("channel").ShouldBe(2);
    }

    [Test]
    public async Task Startup_AccountFailureInOneChannel_DoesNotSeriallyBlockHealthySetup()
    {
        var releaseFailure = Channel.CreateUnbounded<bool>();
        var failure = new IOException("oauth:secret account lookup failed");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueAccount(
            "bad",
            async cancellationToken =>
            {
                _ = await releaseFailure.Reader.ReadAsync(cancellationToken);
                throw failure;
            }
        );
        for (var attempt = 0; attempt < 3; attempt++)
        {
            operations.EnqueueAccountFailure("bad", failure);
        }

        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["bad", "good"], CancellationToken.None);
        var initialization = harness.Session.DrainAsync();

        initialization.IsCompleted.ShouldBeFalse();
        var healthyDuringFailure = (
            await harness.Diagnostics.NextAsync()
        ).ShouldBeOfType<EventSubChannelStatus.Healthy>();
        operations.CreateCount("good").ShouldBe(1);
        AssertHealthy(
            healthyDuringFailure,
            "good",
            EventSubChannelRecoveryTrigger.Startup,
            attempt: 1,
            Now
        );
        releaseFailure.Writer.TryWrite(true).ShouldBeTrue();
        await initialization;

        var states = harness.Status.Current.Channels.ToDictionary(state => state.Channel);
        AssertHealthy(
            states["good"].ShouldBeOfType<EventSubChannelStatus.Healthy>(),
            "good",
            EventSubChannelRecoveryTrigger.Startup,
            attempt: 1,
            Now
        );
        AssertFailure(
            states["bad"].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "bad",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 3,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        states["bad"].ToString().ShouldNotContain("oauth:secret");
        harness
            .RuntimeStatus.Current.ShouldBeOfType<BotRuntimeStatus.Connected>()
            .Channels.ShouldBe(["good"]);
        operations.CompleteStopCount("bad").ShouldBe(0);
    }

    [Test]
    public async Task Startup_ChannelAttemptTimeout_DoesNotInterruptHealthySibling()
    {
        var enteredAttempt = Channel.CreateUnbounded<bool>();
        var neverCompletes = Channel.CreateUnbounded<bool>();
        var operations = new ScriptedChannelOperations();
        operations.EnqueueAccount(
            "slow",
            async cancellationToken =>
            {
                enteredAttempt.Writer.TryWrite(true).ShouldBeTrue();
                _ = await neverCompletes.Reader.ReadAsync(cancellationToken);
                return new BotAccount("slow-bot", "slow-secret");
            }
        );
        await using var harness = CreateHarness(operations, attemptLimit: 2);

        harness.Session.Start(["good", "slow"], CancellationToken.None);
        var startup = harness.Session.DrainAsync();
        _ = await enteredAttempt.Reader.ReadAsync();
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await startup;

        var timeout = harness
            .Diagnostics.Reports.OfType<EventSubChannelStatus.Degraded>()
            .ShouldHaveSingleItem();
        AssertFailure(
            timeout,
            "slow",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Timeout,
            typeof(TimeoutRejectedException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.BeginRecoveryCycle,
            Now.AddMinutes(1)
        );
        harness.Status.Current.Channels.ShouldAllBe(state =>
            state is EventSubChannelStatus.Healthy
        );
        harness.Session.ActiveChannels.ShouldBe(["good", "slow"]);
    }

    [Test]
    public async Task Startup_SubscriptionSetupFailure_PublishesTerminalDegradedPayload()
    {
        var failure = new InvalidOperationException("raw payload must stay private");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueCreateFailure("channel", failure);
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionSetup,
            EventSubChannelFailureClassification.Terminal,
            typeof(InvalidOperationException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        degraded.ToString().ShouldNotContain("raw payload");
        var diagnostic = harness
            .Diagnostics.DiagnosticReports.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>();
        ClassifiedFailure(diagnostic.Failure).Exception.ShouldBeSameAs(failure);
        _ = harness.RuntimeStatus.Current.ShouldBeOfType<BotRuntimeStatus.Authorized>();
        harness.Session.ActiveChannels.ShouldBeEmpty();
        operations.CreateCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(0);
        harness.Status.Current.Channels.ShouldHaveSingleItem().ShouldBeSameAs(degraded);
    }

    [Test]
    public async Task Startup_MissingChannelIdentity_IsTerminalWithoutSubscriptionRetry()
    {
        var operations = new ScriptedChannelOperations();
        operations.EnqueueCreateOutcome(
            "channel",
            new EventSubSubscriptionSetupOutcome.MissingChannel()
        );
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionSetup,
            EventSubChannelFailureClassification.Terminal,
            "MissingChannel",
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        _ = harness
            .Diagnostics.DiagnosticReports.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.MissingChannel>();
        operations.CreateCount("channel").ShouldBe(1);
        operations.StartupDeliveryCount("channel").ShouldBe(0);
        degraded.ToString().ShouldNotContain("channel-secret");
    }

    [Test]
    public async Task Startup_TokenUnavailable_IsTypedTerminalWithoutAccountRetry()
    {
        var operations = new ScriptedChannelOperations();
        operations.EnqueueAccountUnavailable(
            "channel",
            AccessTokenUnavailableReason.MissingRefreshToken
        );
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Terminal,
            nameof(AccessTokenUnavailableReason.MissingRefreshToken),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        harness
            .Diagnostics.DiagnosticReports.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.TokenUnavailable>()
            .Reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        operations.AccountCount("channel").ShouldBe(1);
        operations.CreateCount("channel").ShouldBe(0);
    }

    [Test]
    public async Task Startup_MissingBotIdentity_IsTerminalWithoutSubscriptionRetry()
    {
        var operations = new ScriptedChannelOperations();
        operations.EnqueueCreateOutcome(
            "channel",
            new EventSubSubscriptionSetupOutcome.MissingBot()
        );
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionSetup,
            EventSubChannelFailureClassification.Terminal,
            "MissingBot",
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        _ = harness
            .Diagnostics.DiagnosticReports.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.MissingBot>();
        operations.CreateCount("channel").ShouldBe(1);
        operations.StartupDeliveryCount("channel").ShouldBe(0);
        degraded.ToString().ShouldNotContain("channel-secret");
    }

    [Test]
    public async Task Startup_PublicChatEnqueueRejected_RemainsTerminalAcrossExplicitReconciliation()
    {
        var operations = new ScriptedChannelOperations();
        operations.EnqueueStartupDeliveryOutcome(
            "channel",
            new EventSubStartupDeliveryOutcome.Rejected()
        );
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionSetup,
            EventSubChannelFailureClassification.Terminal,
            "PublicChatEnqueueRejected",
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.NoFurtherAction,
            Now
        );
        _ = harness
            .Diagnostics.DiagnosticReports.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.StartupMessageRejected>();
        operations.CreateCount("channel").ShouldBe(1);
        operations.StartupDeliveryCount("channel").ShouldBe(1);
        operations.ChannelStartedCount("channel").ShouldBe(0);
    }

    [Test]
    public async Task Setup_LifecycleStartFailure_RetriesWithoutRepeatingStartupDelivery()
    {
        var failure = new IOException("lifecycle start temporarily unavailable");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueChannelStartedFailure("channel", failure);
        await using var harness = CreateHarness(operations, attemptLimit: 2);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        operations.StartupDeliveryCount("channel").ShouldBe(1);
        operations.ChannelStartedCount("channel").ShouldBe(2);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        _ = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
        harness
            .Diagnostics.DiagnosticReports.OfType<EventSubChannelDiagnosticReport.Degraded>()
            .ShouldHaveSingleItem()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(failure);
    }

    [Test]
    public async Task Startup_TransientAccountFailure_RecoversIndependently()
    {
        var failure = new IOException("temporary account lookup failure");
        var operations = new ScriptedChannelOperations();
        operations.EnqueueAccountFailure("channel", failure);
        await using var harness = CreateHarness(operations, attemptLimit: 3);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var reports = harness.Diagnostics.Reports;
        AssertFailure(
            reports[0].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.BeginRecoveryCycle,
            Now
        );
        AssertFailure(
            reports[1].ShouldBeOfType<EventSubChannelStatus.Recovering>(),
            "channel",
            EventSubChannelPhase.AccountResolution,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Startup,
            EventSubChannelNextAction.ContinueRecoveryCycle,
            Now
        );
        AssertHealthy(
            reports[2].ShouldBeOfType<EventSubChannelStatus.Healthy>(),
            "channel",
            EventSubChannelRecoveryTrigger.Startup,
            attempt: 1,
            Now
        );
        harness.Status.Current.Channels.ShouldHaveSingleItem().ShouldBe(reports[2]);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task PartialSubscriptionSet_SetupFailure_DeletesEveryCreatedSubscriptionBeforeRetry(
        int createdCount
    )
    {
        var operations = new ScriptedChannelOperations();
        var ids = Enumerable.Range(1, createdCount).Select(static x => $"created-{x}").ToArray();
        operations.EnqueueCreateOutcome(
            "channel",
            new EventSubSubscriptionSetupOutcome.PartiallyCreated(
                new ActiveEventSubSubscription
                {
                    Channel = "channel",
                    SubscriptionId = ids[0],
                    AdditionalSubscriptionIds = ids.Skip(1).ToArray(),
                    BotLogin = "channel-bot",
                    Authorization = EventSubAuthorizationContext.ConfiguredBotAuthority,
                    Readiness = EventSubSubscriptionReadiness.PendingStartupDelivery,
                },
                new HttpRequestException("second subscription failed")
            )
        );
        await using var harness = CreateHarness(operations, attemptLimit: 2);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        var deleted = operations.DeleteAttempts("channel").ShouldHaveSingleItem();
        deleted.AdditionalSubscriptionIds.Count.ShouldBe(createdCount - 1);
        operations.CreateCount("channel").ShouldBe(2);
    }

    [Test]
    public async Task CompleteSubscriptionSet_ChannelRemoval_DeletesChatAndBothShoutoutSubscriptions()
    {
        var operations = new ScriptedChannelOperations();
        operations.EnqueueCreateOutcome(
            "channel",
            new EventSubSubscriptionSetupOutcome.Created(
                new ActiveEventSubSubscription
                {
                    Channel = "channel",
                    SubscriptionId = "chat",
                    AdditionalSubscriptionIds = ["shoutout-create", "shoutout-receive"],
                    BotLogin = "bot",
                    Authorization = EventSubAuthorizationContext.ConfiguredBotAuthority,
                    Readiness = EventSubSubscriptionReadiness.PendingStartupDelivery,
                }
            )
        );
        await using var harness = CreateHarness(operations, attemptLimit: 1);

        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        var deleted = operations.DeleteAttempts("channel").ShouldHaveSingleItem();
        deleted.SubscriptionId.ShouldBe("chat");
        deleted.AdditionalSubscriptionIds.ShouldBe(["shoutout-create", "shoutout-receive"]);
    }
}
