using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task DashboardUiEvents_StartupCancellationSignalsCommittedTimerOnce()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var control = new AutomationCountdownControlService(f.Database, f.Countdowns, f.Catalog);
        _ = await control.ApplyAsync(
            new(f.HostId),
            new("old-process", TimeSpan.FromSeconds(30), CountdownOperation.Start),
            CancellationToken.None
        );
        var hints = 0;
        using var observer = f.UiChanges.Subscribe(
            new AutomationUiChange.Countdown(new(f.HostId), "old-process"),
            ObserverIdentity.Named("startup-committed"),
            async (_, _) =>
            {
                await using var db = await f.Database.CreateDbContextAsync();
                var timer = await db.AutomationCountdowns.AsNoTracking().SingleAsync();
                timer.IsRunning.ShouldBeFalse();
                timer.WasCancelled.ShouldBeTrue();
                hints++;
            }
        );
        var restarted = new AutomationCountdownService(
            f.Database,
            f.Clock,
            () => f.Runtime,
            f.UiChanges
        );
        await restarted.InitializeAsync(CancellationToken.None);
        hints.ShouldBe(1);
        await restarted.InitializeAsync(CancellationToken.None);
        hints.ShouldBe(1);
    }

    [Test]
    public async Task DashboardUiEvents_RunAdmissionWaitingTerminal_ObserveCommittedCompactState_RejectsForeignIdentity()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"go"}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var chat = Node("send-chat", """{"message":"done"}""");
        var flow = await f.SaveAsync(
            [source, delay, chat],
            [Edge(source, "flow", delay), Edge(delay, "complete", chat)]
        );
        var admitted = (
            await new AutomationManualRunService(
                f.Database,
                f.Runtime,
                f.Catalog,
                f.Clock
            ).RunAsync(new(f.HostId), flow, CancellationToken.None)
        ).ShouldBeOfType<AutomationManualRunOutcome.Dispatched>();
        var run = admitted.Dispatch.RunIds.Single();
        var states = new List<AutomationFlowRunState>();
        using var subscription = f.UiChanges.Subscribe(
            new AutomationUiChange.Run(new(f.HostId), run),
            ObserverIdentity.Named("run-observer"),
            async (_, _) =>
            {
                var current = (
                    await f.Queries.ReadStatusAsync(new(f.HostId), [run], CancellationToken.None)
                ).ShouldBeOfType<AutomationRunStatusOutcome.Available>();
                states.Add(current.Runs.Single().State);
            }
        );
        using var foreign = f.UiChanges.Subscribe(
            new AutomationUiChange.Run(new(f.HostId + 1), run),
            ObserverIdentity.Named("foreign"),
            (_, _) => throw new InvalidOperationException("Wrong host notification")
        );
        (await f.Queries.ReadStatusAsync(new(f.HostId), [run], CancellationToken.None))
            .ShouldBeOfType<AutomationRunStatusOutcome.Available>()
            .Runs.Single()
            .State.ShouldBe(AutomationFlowRunState.Waiting);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        states.ShouldBe([AutomationFlowRunState.Completed]);
        f.Chat.Messages.ShouldBe(["done"]);
    }

    [Test]
    public async Task DashboardUiEvents_CountdownCommittedChanges_Idempotency_RemainingTick_AndNativeFinish()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var control = new AutomationCountdownControlService(f.Database, f.Countdowns, f.Catalog);
        var states = new List<AutomationCountdownSnapshot>();
        using var subscription = f.UiChanges.Subscribe(
            new AutomationUiChange.Countdown(new(f.HostId), "tea"),
            ObserverIdentity.Named("countdown-observer"),
            async (_, _) =>
                states.Add(
                    (await control.ReadAsync(new(f.HostId), "tea", CancellationToken.None))
                        .ShouldBeOfType<AutomationCountdownControlOutcome.Available>()
                        .Countdown
                )
        );
        _ = await control.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(4), CountdownOperation.Start),
            CancellationToken.None
        );
        var occurrence = states.Single().OccurrenceId;
        _ = await control.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(4), CountdownOperation.Start),
            CancellationToken.None
        );
        states.Count.ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.Countdowns.TickAsync(await HostForExpandedAsync(f), [], CancellationToken.None);
        states.Count.ShouldBe(1);
        _ = await control.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(2), CountdownOperation.Reset),
            CancellationToken.None
        );
        states.Count.ShouldBe(2);
        states.Last().OccurrenceId.ShouldNotBe(occurrence);
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        await f.Countdowns.TickAsync(await HostForExpandedAsync(f), [], CancellationToken.None);
        states.Last().State.ShouldBe(AutomationCountdownState.Finished);
        _ = await control.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(4), CountdownOperation.Start),
            CancellationToken.None
        );
        _ = await control.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.Zero, CountdownOperation.Cancel),
            CancellationToken.None
        );
        states.Last().State.ShouldBe(AutomationCountdownState.Cancelled);
        var count = states.Count;
        _ = await control.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.Zero, CountdownOperation.Cancel),
            CancellationToken.None
        );
        states.Count.ShouldBe(count);
    }

    [Test]
    public async Task DashboardUiEvents_FeatureCancellation_AutocommitSignals_ImportTransactionDefersAndRollbackDoesNotSignal()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var control = new AutomationCountdownControlService(f.Database, f.Countdowns, f.Catalog);
        _ = await control.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(30), CountdownOperation.Start),
            CancellationToken.None
        );
        var hints = 0;
        using var subscription = f.UiChanges.Subscribe(
            new AutomationUiChange.CountdownHost(new(f.HostId)),
            ObserverIdentity.Named("bulk-observer"),
            (_, _) =>
            {
                hints++;
                return ValueTask.CompletedTask;
            }
        );
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(h => h.Id == f.HostId);
            await using var transaction = await db.Database.BeginTransactionAsync();
            await HostFeatureTransitionStager.StageAsync(
                db,
                host,
                host.EnabledFeatures & ~HostFeatureFlags.Automations,
                f.Clock.GetUtcNow().UtcDateTime,
                f.UiChanges,
                CancellationToken.None
            );
            hints.ShouldBe(0);
            await transaction.RollbackAsync();
        }
        (await control.ReadAsync(new(f.HostId), "tea", CancellationToken.None))
            .ShouldBeOfType<AutomationCountdownControlOutcome.Available>()
            .Countdown.State.ShouldBe(AutomationCountdownState.Running);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(h => h.Id == f.HostId);
            await HostFeatureTransitionStager.StageAsync(
                db,
                host,
                host.EnabledFeatures & ~HostFeatureFlags.Automations,
                f.Clock.GetUtcNow().UtcDateTime,
                f.UiChanges,
                CancellationToken.None
            );
            hints.ShouldBe(1);
            // The ordinary service's later host Save is deliberately not part of this autocommitted cancellation.
        }
        (await control.ReadAsync(new(f.HostId), "tea", CancellationToken.None))
            .ShouldBeOfType<AutomationCountdownControlOutcome.Available>()
            .Countdown.State.ShouldBe(AutomationCountdownState.Cancelled);
        _ = await new CountdownConfigurationImportObserver(f.UiChanges).ImportedAsync(
            f.HostId,
            CancellationToken.None
        );
        hints.ShouldBe(2);
    }

    [Test]
    public async Task DashboardUiEvents_ObserverFailureDoesNotRelabelCommittedCountdown()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        using var failed = f.UiChanges.Subscribe(
            new AutomationUiChange.Countdown(new(f.HostId), "tea"),
            ObserverIdentity.Named("failing"),
            (_, _) => throw new InvalidOperationException("observer failure")
        );
        var control = new AutomationCountdownControlService(f.Database, f.Countdowns, f.Catalog);
        var result = await control.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(4), CountdownOperation.Start),
            CancellationToken.None
        );
        result
            .ShouldBeOfType<AutomationCountdownControlOutcome.Available>()
            .Countdown.State.ShouldBe(AutomationCountdownState.Running);
    }

    [Test]
    public async Task DashboardUiEvents_CountdownEmitFailureStillReportsCommittedStartAndFinish()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var countdowns = new AutomationCountdownService(
            f.Database,
            f.Clock,
            () => throw new IOException("Finite business emission failure"),
            f.UiChanges
        );
        var control = new AutomationCountdownControlService(f.Database, countdowns, f.Catalog);
        var states = new List<AutomationCountdownState>();
        using var observer = f.UiChanges.Subscribe(
            new AutomationUiChange.Countdown(new(f.HostId), "emit-fails"),
            ObserverIdentity.Named("committed-before-emit"),
            async (_, _) =>
            {
                var current = (
                    await control.ReadAsync(new(f.HostId), "emit-fails", CancellationToken.None)
                ).ShouldBeOfType<AutomationCountdownControlOutcome.Available>();
                states.Add(current.Countdown.State);
            }
        );
        _ = await Should.ThrowAsync<IOException>(() =>
            control.ApplyAsync(
                new(f.HostId),
                new("emit-fails", TimeSpan.FromSeconds(1), CountdownOperation.Start),
                CancellationToken.None
            )
        );
        states.ShouldBe([AutomationCountdownState.Running]);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        _ = await Should.ThrowAsync<IOException>(async () =>
            await countdowns.TickAsync(await HostForExpandedAsync(f), [], CancellationToken.None)
        );
        states.ShouldBe([AutomationCountdownState.Running, AutomationCountdownState.Finished]);
    }

    [Test]
    public async Task DashboardUiEvents_OrdinaryFeatureSaveFailureStillReportsAutocommittedTimerCancellation()
    {
        var failure = new DashboardHostSaveFailure();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [failure]);
        var control = new AutomationCountdownControlService(f.Database, f.Countdowns, f.Catalog);
        _ = await control.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(30), CountdownOperation.Start),
            CancellationToken.None
        );
        var states = new List<AutomationCountdownState>();
        using var subscription = f.UiChanges.Subscribe(
            new AutomationUiChange.CountdownHost(new(f.HostId)),
            ObserverIdentity.Named("ordinary-feature-autocommit"),
            async (_, _) =>
                states.Add(
                    (await control.ReadAsync(new(f.HostId), "tea", CancellationToken.None))
                        .ShouldBeOfType<AutomationCountdownControlOutcome.Available>()
                        .Countdown.State
                )
        );
        var events = TestEventBus.Create<AppEventKind>();
        var features = new HostFeatureService(
            f.Database,
            new(
                [],
                new(events),
                new DurableAlertService(f.Database, f.Clock, events),
                NullLogger<HostFeatureActivationAuthority>.Instance
            ),
            f.Clock,
            f.UiChanges
        );
        failure.Armed = true;
        _ = await Should.ThrowAsync<DbUpdateException>(() =>
            features.DisableAsync(f.HostId, HostFeatureFlags.Automations, CancellationToken.None)
        );
        states.ShouldBe([AutomationCountdownState.Cancelled]);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.Hosts.SingleAsync(h => h.Id == f.HostId))
            .EnabledFeatures.HasFlag(HostFeatureFlags.Automations)
            .ShouldBeTrue();
        (await db.AutomationCountdowns.SingleAsync()).WasCancelled.ShouldBeTrue();
    }

    private sealed class DashboardHostSaveFailure : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data,
            InterceptionResult<int> result,
            CancellationToken ct = default
        ) =>
            Armed
            && data.Context!.ChangeTracker.Entries<BotHost>()
                .Any(e => e.State == EntityState.Modified)
                ? ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("Finite host-save failure")
                )
                : ValueTask.FromResult(result);
    }

    private static IDisposable TrackCommittedRunStates(
        RuntimeFixture fixture,
        AutomationRunId run,
        List<AutomationFlowRunState> states
    ) =>
        fixture.UiChanges.Subscribe(
            new AutomationUiChange.Run(new(fixture.HostId), run),
            ObserverIdentity.Named($"committed-status-{Guid.NewGuid():N}"),
            async (_, ct) =>
            {
                ct.ShouldBe(CancellationToken.None);
                var current = (
                    await fixture.Queries.ReadStatusAsync(new(fixture.HostId), [run], ct)
                ).ShouldBeOfType<AutomationRunStatusOutcome.Available>();
                states.Add(current.Runs.Single().State);
            }
        );
}
