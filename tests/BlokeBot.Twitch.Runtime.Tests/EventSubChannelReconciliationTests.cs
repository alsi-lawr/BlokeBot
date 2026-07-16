using System.Threading.Channels;
using BlokeBot.Commands;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Timeout;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubChannelReconciliationTests : EventSubChannelRecoveryTestBase
{
    [Test]
    public async Task Reconciliation_TransientDeleteFailure_RecoversThroughOwnedPolicy()
    {
        var failure = new IOException("remote delete failed once");
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueDeleteFailure("channel", failure);
        harness.Diagnostics.Clear();

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

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
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueDeleteFailure("channel", failure);
        harness.Diagnostics.Clear();

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

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
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            operations.EnqueueAccountResult("channel", "replacement-bot");
            operations.EnqueueDeleteFailure("channel", failure);
        }

        harness.Diagnostics.Clear();
        harness.Session.TriggerReconciliation(["channel"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

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
        pending.Subscription.SubscriptionId.ShouldBe("session-id-channel");
        pending.Subscription.AccessToken.ShouldBe("channel-secret");
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
        harness.Session.Start(["bad", "good"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueDeleteFailure("bad", failure);
        harness.Diagnostics.Clear();

        harness.Session.TriggerReconciliation(["good"], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("bad").ShouldBe(1);
        operations.CompleteStopCount("bad").ShouldBe(0);
        harness.Session.ActiveChannels.ShouldBe(["bad", "good"]);
        var states = harness.Status.Current.Channels.ToDictionary(state => state.Channel);
        states["good"].ShouldBeOfType<EventSubChannelStatus.Healthy>();
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
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(1);
        harness.Session.ActiveChannels.ShouldBeEmpty();
        harness.PendingDeletions.PendingDeletions.ShouldBeEmpty();
        harness.Status.Current.Channels.ShouldBeEmpty();
        harness.RuntimeStatus.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();
    }

    [Test]
    public async Task Reconciliation_StopFailure_RetriesWithoutRepeatingRemoteDelete()
    {
        var operations = new ScriptedChannelOperations();
        await using var harness = CreateHarness(operations, attemptLimit: 2);
        harness.Session.Start(["channel"], CancellationToken.None);
        await harness.Session.DrainAsync();
        operations.EnqueueCompleteStopFailure(
            "channel",
            new IOException("lifecycle store temporarily unavailable")
        );

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await harness.Session.DrainAsync();

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
        harness.Session.Start(["channel"], cancellation.Token);
        await harness.Session.DrainAsync();
        operations.EnqueueBeforeDelete("channel", cancellation.Cancel);
        operations.EnqueueDeleteFailure(
            "channel",
            new OperationCanceledException(cancellation.Token)
        );

        harness.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            harness.Session.DrainAsync
        );

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        operations.DeleteCount("channel").ShouldBe(1);
        operations.CompleteStopCount("channel").ShouldBe(0);
        harness.Session.ActiveChannels.ShouldBe(["channel"]);
        harness
            .PendingDeletions.PendingDeletions.ShouldHaveSingleItem()
            .State.ShouldBeOfType<EventSubPendingDeletionState.Scheduled>();
        harness
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
        old.Session.Start(["channel"], CancellationToken.None);
        await old.Session.DrainAsync();
        oldOperations.EnqueueDeleteFailure(
            "channel",
            new InvalidOperationException("old session delete secret")
        );
        old.Session.TriggerReconciliation([], EventSubChannelRecoveryTrigger.Explicit);
        await old.Session.DrainAsync();
        sharedPendingDeletions.PendingDeletions.ShouldHaveSingleItem();
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
        replacement.Session.Start([], CancellationToken.None);
        await replacement.Session.DrainAsync();

        replacementOperations.DeleteCount("channel").ShouldBe(1);
        var deleted = replacementOperations.DeleteAttempts("channel").ShouldHaveSingleItem();
        deleted.SubscriptionId.ShouldBe("session-id-channel");
        deleted.AccessToken.ShouldBe("channel-secret");
        replacementOperations.CompleteStopCount("channel").ShouldBe(1);
        sharedPendingDeletions.PendingDeletions.ShouldBeEmpty();
        sharedPendingDeletions.HasPendingReconciliation.ShouldBeFalse();
        replacement.Session.ActiveChannels.ShouldBeEmpty();
        sharedStatus.Current.Channels.ShouldBeEmpty();
        sharedRuntimeStatus.Current.ShouldBeOfType<BotRuntimeStatus.Unauthorized>();
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
        old.Session.Start(["old"], CancellationToken.None);
        await old.Session.DrainAsync();
        oldOperations.EnqueueAccount(
            "old",
            async cancellationToken =>
            {
                enteredRecovery.Writer.TryWrite(true).ShouldBeTrue();
                await releaseRecovery.Reader.ReadAsync(cancellationToken);
                return new BotAccount("old-bot", "old-secret");
            }
        );
        old.Session.TriggerReconciliation(["old"], EventSubChannelRecoveryTrigger.Explicit);
        await enteredRecovery.Reader.ReadAsync();

        var replacementOperations = new ScriptedChannelOperations();
        await using var replacement = CreateHarness(
            replacementOperations,
            attemptLimit: 2,
            sharedStatus,
            sharedRuntimeStatus
        );
        replacement.Session.Start(["replacement"], CancellationToken.None);
        await replacement.Session.DrainAsync();

        await old.Session.DisposeAsync();
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
