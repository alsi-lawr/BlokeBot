using System.Threading.Channels;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelReconciliationTests : EventSubChannelRecoveryTestBase
{
    [Test]
    public async Task ReplacedRuntimeSession_StartsAndLaterStopsOnlyNewIdentity()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        Start(harness, ["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        var first = operations.ChannelStartedTargets.ShouldHaveSingleItem();
        var second = ReplaceTarget(harness.Session, "channel");

        await ReconcileAsync(harness.Session, ["channel"], EventSubChannelRecoveryTrigger.Explicit);
        operations.CompleteStopTargets.ShouldBeEmpty();
        ReferenceEquals(operations.ChannelStartedTargets[1].SessionIdentity, second.SessionIdentity)
            .ShouldBeTrue();
        ReferenceEquals(first.SessionIdentity, second.SessionIdentity).ShouldBeFalse();

        await ReconcileAsync(harness.Session, [], EventSubChannelRecoveryTrigger.Explicit);

        ReferenceEquals(
                operations.CompleteStopTargets.ShouldHaveSingleItem().SessionIdentity,
                second.SessionIdentity
            )
            .ShouldBeTrue();
    }

    [Test]
    public async Task PeriodicInventoryHealth_RecreatesTrackedChannelWithNoEnabledOwnedId()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        Start(harness, ["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        await harness.Session.RepairMissingSubscriptionsAndDrainAsync(
            _ => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal)),
            _ => ValueTask.FromResult(TargetsFor(harness.Session, ["channel"])),
            CancellationToken.None
        );

        operations.CreateCount("channel").ShouldBe(2);
        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(0);
        _ = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
    }

    [Test]
    public async Task PeriodicInventoryHealth_RecreatesChannelAbsentFromEnabledIdsAndKeepsSiblingHealthy()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        Start(harness, ["missing", "healthy"], CancellationToken.None);
        await harness.Session.DrainAsync();
        harness.Diagnostics.Clear();

        await harness.Session.RepairMissingSubscriptionsAndDrainAsync(
            _ =>
                Task.FromResult<IReadOnlySet<string>>(
                    new HashSet<string>(StringComparer.Ordinal) { "subscription-healthy" }
                ),
            _ => ValueTask.FromResult(TargetsFor(harness.Session, ["missing", "healthy"])),
            CancellationToken.None
        );

        operations.CreateCount("missing").ShouldBe(2);
        operations.DeleteCount("missing").ShouldBe(1);
        operations.CreateCount("healthy").ShouldBe(1);
        operations.DeleteCount("healthy").ShouldBe(0);
        harness.Status.Current.Channels.ShouldAllBe(status =>
            status is EventSubChannelStatus.Healthy
        );
    }

    [Test]
    public async Task QueuedPeriodicInventory_LoadsEvidenceAfterEarlierRepairCompletes()
    {
        var enteredReplacement = Channel.CreateUnbounded<bool>();
        var releaseReplacement = Channel.CreateUnbounded<bool>();
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        Start(harness, ["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueAccount(
            "channel",
            async cancellationToken =>
            {
                enteredReplacement.Writer.TryWrite(true).ShouldBeTrue();
                _ = await releaseReplacement.Reader.ReadAsync(cancellationToken);
                return new BotAccount("channel-bot", "channel-secret");
            }
        );

        var replacement = harness.Session.RepairRevokedSubscriptionAndDrainAsync(
            "subscription-channel",
            _ => ValueTask.FromResult(TargetsFor(harness.Session, ["channel"])),
            CancellationToken.None
        );
        _ = await enteredReplacement.Reader.ReadAsync();
        var inventoryLoads = 0;
        var desiredChannelLoads = 0;

        var periodic = harness.Session.RepairMissingSubscriptionsAndDrainAsync(
            ignored =>
            {
                _ = Interlocked.Increment(ref inventoryLoads);
                return Task.FromResult<IReadOnlySet<string>>(
                    new HashSet<string>(StringComparer.Ordinal) { "subscription-channel" }
                );
            },
            ignored =>
            {
                _ = Interlocked.Increment(ref desiredChannelLoads);
                return ValueTask.FromResult(TargetsFor(harness.Session, ["channel"]));
            },
            CancellationToken.None
        );

        Volatile.Read(ref inventoryLoads).ShouldBe(0);
        Volatile.Read(ref desiredChannelLoads).ShouldBe(0);
        releaseReplacement.Writer.TryWrite(true).ShouldBeTrue();
        await replacement;
        await periodic;

        Volatile.Read(ref inventoryLoads).ShouldBe(1);
        Volatile.Read(ref desiredChannelLoads).ShouldBe(1);
        operations.CreateCount("channel").ShouldBe(2);
        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(0);
        _ = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
    }

    [Test]
    public async Task Revocation_RecreatesOwningChannelWithoutTouchingSibling()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 1);
        Start(harness, ["revoked", "healthy"], CancellationToken.None);
        await harness.Session.DrainAsync();

        await harness.Session.RepairRevokedSubscriptionAndDrainAsync(
            "subscription-revoked",
            _ => ValueTask.FromResult(TargetsFor(harness.Session, ["revoked", "healthy"])),
            CancellationToken.None
        );

        operations.CreateCount("revoked").ShouldBe(2);
        operations.DeleteCount("revoked").ShouldBe(1);
        operations.CreateCount("healthy").ShouldBe(1);
        operations.DeleteCount("healthy").ShouldBe(0);
    }

    [Test]
    public async Task Reconciliation_TransientDeleteFailure_RecoversThroughOwnedPolicy()
    {
        var failure = new IOException("remote delete failed once");
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        Start(harness, ["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueDeleteFailure("channel", failure);
        harness.Diagnostics.Clear();

        await ReconcileAsync(harness.Session, [], EventSubChannelRecoveryTrigger.Explicit);

        operations.DeleteCount("channel").ShouldBe(2);
        operations.CompleteStopCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.PendingDeletions.PendingDeletions.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
        var reports = harness.Diagnostics.Reports;
        AssertFailure(
            reports[0].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.BeginRecoveryCycle,
            Now
        );
        AssertFailure(
            reports[1].ShouldBeOfType<EventSubChannelStatus.Recovering>(),
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.ContinueRecoveryCycle,
            Now
        );
        var diagnostics = harness.Diagnostics.DiagnosticReports;
        diagnostics[0]
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Degraded>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(failure);
        diagnostics[1]
            .ShouldBeOfType<EventSubChannelDiagnosticReport.Recovering>()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(failure);
    }

    [Test]
    public async Task Reconciliation_TimeoutDeleteFailure_RecoversThroughOwnedPolicy()
    {
        var failure = new TimeoutException("remote delete timed out once");
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        Start(harness, ["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueDeleteFailure("channel", failure);
        harness.Diagnostics.Clear();

        await ReconcileAsync(harness.Session, [], EventSubChannelRecoveryTrigger.Explicit);

        operations.DeleteCount("channel").ShouldBe(2);
        operations.CompleteStopCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.PendingDeletions.PendingDeletions.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
        var reports = harness.Diagnostics.Reports;
        AssertFailure(
            reports[0].ShouldBeOfType<EventSubChannelStatus.Degraded>(),
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Timeout,
            typeof(TimeoutException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.BeginRecoveryCycle,
            Now
        );
        AssertFailure(
            reports[1].ShouldBeOfType<EventSubChannelStatus.Recovering>(),
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Timeout,
            typeof(TimeoutException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.ContinueRecoveryCycle,
            Now
        );
    }

    [Test]
    public async Task Reconciliation_TransientDeleteExhaustion_RetainsPendingEvidence()
    {
        var failure = new IOException("remote delete secret");
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        Start(harness, ["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            operations.EnqueueAccountResult("channel", "replacement-bot");
            operations.EnqueueDeleteFailure("channel", failure);
        }

        harness.Diagnostics.Clear();
        await ReconcileAsync(harness.Session, ["channel"], EventSubChannelRecoveryTrigger.Explicit);

        operations.DeleteCount("channel").ShouldBe(3);
        operations.CreateCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        var degraded = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "channel",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Transient,
            typeof(IOException),
            attempt: 2,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        operations.CompleteStopCount("channel").ShouldBe(0);
        var pending = harness.PendingDeletions.PendingDeletions.ShouldHaveSingleItem();
        pending.Subscription.Channel.ShouldBe("channel");
        pending.Subscription.SubscriptionId.ShouldBe("subscription-channel");
        var unresolved = pending.State.ShouldBeOfType<EventSubPendingDeletionState.Unresolved>();
        unresolved.Failure.Classification.ShouldBe(EventSubChannelFailureClassification.Transient);
        unresolved.Failure.Exception.ShouldBeSameAs(failure);
        pending.ToString().ShouldNotContain("channel-secret");
        pending.ToString().ShouldNotContain("remote delete secret");
        degraded.ToString().ShouldNotContain("remote delete secret");
    }

    [Test]
    public async Task Reconciliation_TerminalDeleteFailure_DegradesOnlyOwningChannel()
    {
        var failure = new InvalidOperationException("terminal delete secret");
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        Start(harness, ["bad", "good"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueDeleteFailure("bad", failure);
        harness.Diagnostics.Clear();

        await ReconcileAsync(harness.Session, ["good"], EventSubChannelRecoveryTrigger.Explicit);

        operations.DeleteCount("bad").ShouldBe(1);
        operations.CompleteStopCount("bad").ShouldBe(0);
        harness.Session.ActiveChannels.ShouldBe(["bad", "good"]);
        var states = harness.Status.Current.Channels.ToDictionary(static state => state.Channel);
        _ = states["good"].ShouldBeOfType<EventSubChannelStatus.Healthy>();
        var degraded = states["bad"].ShouldBeOfType<EventSubChannelStatus.Degraded>();
        AssertFailure(
            degraded,
            "bad",
            EventSubChannelPhase.SubscriptionDeletion,
            EventSubChannelFailureClassification.Terminal,
            typeof(InvalidOperationException),
            attempt: 1,
            EventSubChannelRecoveryTrigger.Explicit,
            EventSubChannelNextAction.RetryOnNextReconciliation,
            Now
        );
        degraded.ToString().ShouldNotContain("terminal delete secret");
        harness
            .RuntimeStatus.Current.ShouldBeOfType<BotRuntimeStatus.Connected>()
            .Channels.ShouldBe(["good"]);
        var pending = harness.PendingDeletions.PendingDeletions.ShouldHaveSingleItem();
        pending.Subscription.Channel.ShouldBe("bad");
        pending
            .State.ShouldBeOfType<EventSubPendingDeletionState.Unresolved>()
            .Failure.Exception.ShouldBeSameAs(failure);
        harness
            .Diagnostics.DiagnosticReports.OfType<EventSubChannelDiagnosticReport.Degraded>()
            .ShouldHaveSingleItem()
            .Failure.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>()
            .Details.Exception.ShouldBeSameAs(failure);
    }

    [Test]
    public async Task Reconciliation_RemovingHealthyChannel_DeletesThenReportsStopped()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        Start(harness, ["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        await ReconcileAsync(harness.Session, [], EventSubChannelRecoveryTrigger.Explicit);

        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.PendingDeletions.PendingDeletions.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
        _ = harness.RuntimeStatus.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();
    }

    [Test]
    public async Task Reconciliation_StopFailure_RetriesWithoutRepeatingRemoteDelete()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        Start(harness, ["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueCompleteStopFailure(
            "channel",
            new IOException("lifecycle store temporarily unavailable")
        );

        await ReconcileAsync(harness.Session, [], EventSubChannelRecoveryTrigger.Explicit);

        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(2);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
    }

    [Test]
    public async Task Reconciliation_DeleteCancellation_PreservesScheduledEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        Start(harness, ["channel"], cancellation.Token);
        await harness.Session.DrainAsync();
        operations.EnqueueBeforeDelete("channel", cancellation.Cancel);
        operations.EnqueueDeleteFailure(
            "channel",
            new OperationCanceledException(cancellation.Token)
        );

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() =>
            ReconcileAsync(harness.Session, [], EventSubChannelRecoveryTrigger.Explicit)
        );

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(0);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        _ = harness
            .PendingDeletions.PendingDeletions.ShouldHaveSingleItem()
            .State.ShouldBeOfType<EventSubPendingDeletionState.Scheduled>();
        _ = harness
            .Status.Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
    }

    [Test]
    public async Task Startup_PendingDeletionFromReplacedSession_ReconcilesBeforeRemoval()
    {
        var sharedStatus = new EventSubChannelStatusStore();
        var sharedRuntimeStatus = new BotRuntimeStatusStore();
        var sharedPendingDeletions = new EventSubSubscriptionReconciliationStore();
        var oldOperations = new ScriptedChannelOperations();
        await using var old = CreateHarness(
            oldOperations,
            attemptLimit: 2,
            sharedStatus,
            sharedRuntimeStatus,
            sharedPendingDeletions
        );
        Start(old, ["channel"], CancellationToken.None);
        await old.Session.DrainAsync();
        oldOperations.EnqueueDeleteFailure(
            "channel",
            new InvalidOperationException("old session delete secret")
        );
        await ReconcileAsync(old.Session, [], EventSubChannelRecoveryTrigger.Explicit);
        _ = sharedPendingDeletions.PendingDeletions.ShouldHaveSingleItem();
        sharedPendingDeletions.HasPendingReconciliation.ShouldBeTrue();
        await old.DisposeAsync();

        var replacementOperations = new ScriptedChannelOperations();
        await using var replacement = CreateHarness(
            replacementOperations,
            attemptLimit: 2,
            sharedStatus,
            sharedRuntimeStatus,
            sharedPendingDeletions
        );
        Start(replacement, [], CancellationToken.None);
        await replacement.Session.DrainAsync();

        replacementOperations.DeleteCount("channel").ShouldBe(1);
        var deleted = replacementOperations.DeleteAttempts("channel").ShouldHaveSingleItem();
        deleted.SubscriptionId.ShouldBe("subscription-channel");
        replacementOperations.CompleteStopCount("channel").ShouldBe(1);
        sharedPendingDeletions.PendingDeletions.ShouldBeEmpty();
        sharedPendingDeletions.HasPendingReconciliation.ShouldBeFalse();
        replacement.Session.ActiveChannels.ShouldBeEmpty();
        sharedStatus.Current.Channels.ShouldBeEmpty();
        _ = sharedRuntimeStatus.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();
    }

    [Test]
    public async Task ReplacementStartup_DisposingOldPendingRecovery_PreventsPostDisposalMutation()
    {
        var sharedStatus = new EventSubChannelStatusStore();
        var sharedRuntimeStatus = new BotRuntimeStatusStore();
        var oldOperations = new ScriptedChannelOperations();
        var enteredRecovery = Channel.CreateUnbounded<bool>();
        var releaseRecovery = Channel.CreateUnbounded<bool>();
        await using var old = CreateHarness(
            oldOperations,
            attemptLimit: 2,
            sharedStatus,
            sharedRuntimeStatus
        );
        Start(old, ["old"], CancellationToken.None);
        await old.Session.DrainAsync();
        oldOperations.EnqueueAccount(
            "old",
            async cancellationToken =>
            {
                enteredRecovery.Writer.TryWrite(true).ShouldBeTrue();
                _ = await releaseRecovery.Reader.ReadAsync(cancellationToken);
                return new BotAccount("old-bot", "old-secret");
            }
        );
        var oldReconciliation = ReconcileAsync(
            old.Session,
            ["old"],
            EventSubChannelRecoveryTrigger.Explicit
        );
        _ = await enteredRecovery.Reader.ReadAsync();

        var replacementOperations = new ScriptedChannelOperations();
        await using var replacement = CreateHarness(
            replacementOperations,
            attemptLimit: 2,
            sharedStatus,
            sharedRuntimeStatus
        );
        Start(replacement, ["replacement"], CancellationToken.None);
        await replacement.Session.DrainAsync();

        await old.Session.DisposeAsync();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => oldReconciliation);
        releaseRecovery.Writer.TryWrite(true).ShouldBeTrue();

        var replacementState = sharedStatus
            .Current.Channels.ShouldHaveSingleItem()
            .ShouldBeOfType<EventSubChannelStatus.Healthy>();
        replacementState.Channel.ShouldBe("replacement");
        sharedRuntimeStatus
            .Current.ShouldBeOfType<BotRuntimeStatus.Connected>()
            .Channels.ShouldBe(["replacement"]);
        oldOperations.CreateCount("old").ShouldBe(1);
        old.Session.ActiveChannels.ShouldBe(["old"]);
        replacement.Session.ActiveChannels.ShouldBe(["replacement"]);
    }
}
