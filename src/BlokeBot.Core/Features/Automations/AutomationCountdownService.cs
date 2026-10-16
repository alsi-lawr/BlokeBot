using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using static BlokeBot.Core.Features.Automations.ExpandedAutomationContext;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationCountdownService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock,
    Func<AutomationRuntimeService> runtime,
    IAutomationUiChangePublisher uiChanges
)
{
    internal Guid ProcessId { get; } = Guid.NewGuid();
    internal DateTimeOffset UtcNow => clock.GetUtcNow();
    private readonly Lock _initializationGate = new();
    private Task? _initialization;

    internal Task InitializeAsync(CancellationToken cancellation)
    {
        Task initialization;
        lock (_initializationGate)
        {
            if (_initialization is { IsFaulted: true } or { IsCanceled: true })
            {
                _initialization = null;
            }
            initialization = _initialization ??= CancelOldProcessesAsync();
        }
        return initialization.WaitAsync(cancellation);
    }

    private async Task CancelOldProcessesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var changed = await db
            .AutomationCountdowns.Where(t =>
                t.ProcessId != ProcessId && (t.IsRunning || t.OwnerViewerId != "")
            )
            .Select(t => new
            {
                t.HostId,
                t.Name,
                t.OwnerViewerId,
            })
            .ToArrayAsync();
        var updated = await db
            .AutomationCountdowns.Where(t =>
                t.IsRunning && t.ProcessId != ProcessId && t.OwnerViewerId == ""
            )
            .ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.IsRunning, false).SetProperty(t => t.WasCancelled, true)
            );
        updated += await db
            .AutomationCountdowns.Where(t => t.ProcessId != ProcessId && t.OwnerViewerId != "")
            .ExecuteDeleteAsync();
        if (updated > 0)
        {
            foreach (var timer in changed)
            {
                await NotifyAsync(new(timer.HostId), timer.Name, timer.OwnerViewerId);
            }
        }
    }

    internal Task<AutomationCountdownApplyOutcome> ApplyAsync(
        AutomationHostId hostId,
        CountdownActionConfiguration action,
        CancellationToken cancellation
    ) =>
        AutomationNativeActionPreparation
            .Countdown(
                action,
                new Dictionary<AutomationConfigurationFieldId, AutomationResolvedValue>(),
                null,
                UtcNow
            )
            .Match(
                prepared => ApplyPreparedAsync(hostId, prepared.Payload, cancellation),
                _ =>
                    Task.FromResult<AutomationCountdownApplyOutcome>(
                        new AutomationCountdownApplyOutcome.Unavailable()
                    )
            );

    internal async Task<AutomationCountdownApplyOutcome> ApplyPreparedAsync(
        AutomationHostId hostId,
        AutomationPreparedCountdown prepared,
        CancellationToken cancellation
    )
    {
        var action = prepared.Action;
        await InitializeAsync(cancellation);
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
        if (await MainDatabaseStatements.LockHostAsync(db, hostId.Value, cancellation) == 0)
        {
            return new AutomationCountdownApplyOutcome.Unavailable();
        }
        var host = await db
            .Hosts.AsNoTracking()
            .SingleAsync(h => h.Id == hostId.Value, cancellation);
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return new AutomationCountdownApplyOutcome.Unavailable();
        }
        var timer = await db.AutomationCountdowns.SingleOrDefaultAsync(
            t =>
                t.HostId == hostId.Value
                && t.OwnerViewerId == prepared.OwnerViewerId
                && t.Name == action.Name,
            cancellation
        );
        if (
            (prepared.ExpectedOccurrence is { } expected && timer?.OccurrenceId != expected)
            || (
                prepared.OwnerViewerId.Length > 0
                && action.Operation != CountdownOperation.Start
                && timer is null
            )
        )
        {
            return new AutomationCountdownApplyOutcome.Unavailable();
        }
        var running =
            timer is { IsRunning: true }
            && timer.ProcessId == ProcessId
            && timer.AutomationGeneration == host.AutomationGeneration;
        if (action.Operation == CountdownOperation.Start && running)
        {
            return new AutomationCountdownApplyOutcome.AlreadyRunning();
        }
        if (action.Operation == CountdownOperation.Cancel && !running)
        {
            return new AutomationCountdownApplyOutcome.Applied();
        }
        var now = clock.GetUtcNow();
        if (
            action.Operation != CountdownOperation.Cancel
            && action.Duration.Ticks > DateTime.MaxValue.Ticks - now.UtcTicks
        )
        {
            return new AutomationCountdownApplyOutcome.Unavailable();
        }
        timer ??= new()
        {
            HostId = hostId.Value,
            Name = action.Name,
            OwnerViewerId = prepared.OwnerViewerId,
            OwnerDisplayName = prepared.OwnerDisplayName,
        };
        if (action.Operation == CountdownOperation.Cancel)
        {
            timer.IsRunning = false;
            timer.WasCancelled = true;
        }
        else
        {
            timer.OccurrenceId = Guid.NewGuid();
            timer.ProcessId = ProcessId;
            timer.AutomationGeneration = host.AutomationGeneration;
            timer.IsRunning = true;
            timer.WasCancelled = false;
            timer.StartedAtUtc = now.UtcDateTime;
            timer.ObservedAtUtc = now.UtcDateTime;
            timer.DeadlineUtc = (now + action.Duration).UtcDateTime;
        }
        if (db.Entry(timer).State == EntityState.Detached)
        {
            _ = db.AutomationCountdowns.Add(timer);
        }
        _ = await db.SaveChangesAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        await NotifyAsync(hostId, timer.Name, timer.OwnerViewerId);
        var kind = action.Operation switch
        {
            CountdownOperation.Start => CountdownLifecycle.Started,
            CountdownOperation.Reset => CountdownLifecycle.Reset,
            CountdownOperation.Cancel => CountdownLifecycle.Cancelled,
        };
        _ = await EmitAsync(host, timer, kind, TimeSpan.Zero, now, cancellation);
        return action.Operation == CountdownOperation.Start
            ? new AutomationCountdownApplyOutcome.Started()
            : new AutomationCountdownApplyOutcome.Applied();
    }

    internal async Task TickAsync(
        BotHost host,
        IReadOnlyList<CountdownSourceConfiguration> sources,
        CancellationToken cancellation
    )
    {
        await InitializeAsync(cancellation);
        var emitted =
            new List<(AutomationCountdown Timer, CountdownLifecycle Event, TimeSpan Threshold)>();
        var changed = new HashSet<(string Name, string Owner)>();
        var now = clock.GetUtcNow();
        await using (var db = await dbFactory.CreateDbContextAsync(cancellation))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
            if (await MainDatabaseStatements.LockHostAsync(db, host.Id, cancellation) == 0)
            {
                return;
            }
            var current = await db
                .Hosts.AsNoTracking()
                .SingleAsync(h => h.Id == host.Id, cancellation);
            var timers = await db
                .AutomationCountdowns.Where(t => t.HostId == host.Id && t.IsRunning)
                .ToArrayAsync(cancellation);
            foreach (var timer in timers)
            {
                if (
                    !current.EnabledFeatures.Contains(HostFeatureFlags.Automations)
                    || timer.ProcessId != ProcessId
                    || timer.AutomationGeneration != current.AutomationGeneration
                )
                {
                    timer.IsRunning = false;
                    timer.WasCancelled = true;
                    _ = changed.Add((timer.Name, timer.OwnerViewerId));
                    if (timer.OwnerViewerId.Length > 0)
                    {
                        _ = db.AutomationCountdowns.Remove(timer);
                    }
                    continue;
                }
                foreach (
                    var threshold in sources
                        .Where(s =>
                            s.Matches(timer.Name, timer.OwnerViewerId)
                            && s.Event == CountdownLifecycle.Remaining
                        )
                        .Select(s => s.Remaining)
                        .Distinct()
                )
                {
                    if (
                        timer.ObservedAtUtc < timer.DeadlineUtc - threshold
                        && now.UtcDateTime >= timer.DeadlineUtc - threshold
                    )
                    {
                        emitted.Add((timer, CountdownLifecycle.Remaining, threshold));
                    }
                }
                timer.ObservedAtUtc = now.UtcDateTime;
                if (timer.DeadlineUtc <= now.UtcDateTime)
                {
                    timer.IsRunning = false;
                    _ = changed.Add((timer.Name, timer.OwnerViewerId));
                    emitted.Add((timer, CountdownLifecycle.Finished, TimeSpan.Zero));
                }
            }
            _ = await db.SaveChangesAsync(cancellation);
            await transaction.CommitAsync(cancellation);
        }
        foreach (var timer in changed)
        {
            await NotifyAsync(new(host.Id), timer.Name, timer.Owner);
        }
        foreach (var e in emitted)
        {
            _ = await EmitAsync(host, e.Timer, e.Event, e.Threshold, now, cancellation);
        }
    }

    private async Task NotifyAsync(AutomationHostId host, string name, string owner)
    {
        await uiChanges.CountdownChangedAsync(host, name);
        if (owner.Length > 0)
        {
            await uiChanges.CountdownsChangedAsync(host);
        }
    }

    private async Task<AutomationDispatchOutcome> EmitAsync(
        BotHost host,
        AutomationCountdown timer,
        CountdownLifecycle kind,
        TimeSpan threshold,
        DateTimeOffset now,
        CancellationToken cancellation
    )
    {
        var context = Create(
            host,
            AutomationDefinitionIds.CountdownSource,
            $"{timer.OccurrenceId}:{kind}:{threshold.Ticks}",
            now,
            now,
            [
                Text("timer-name", timer.Name),
                .. timer.OwnerViewerId.Length == 0
                    ? Array.Empty<KeyValuePair<AutomationVariableName, AutomationVariable>>()
                    :
                    [
                        Text("initiating-viewer-id", timer.OwnerViewerId, sensitive: true),
                        Text("initiating-display-name", timer.OwnerDisplayName ?? ""),
                    ],
                Text("timer-occurrence", timer.OccurrenceId.ToString()),
                Text("event-kind", kind.ToString()),
                Time("deadline", new(timer.DeadlineUtc, TimeSpan.Zero)),
                Number(
                    "remaining-seconds",
                    (decimal)Math.Max(0, (timer.DeadlineUtc - now.UtcDateTime).TotalSeconds)
                ),
            ]
        );
        try
        {
            return await runtime()
                .DispatchExpandedAsync(
                    context,
                    c =>
                        c is CountdownSourceConfiguration s
                        && s.Matches(timer.Name, timer.OwnerViewerId)
                        && s.Event == kind
                        && (kind != CountdownLifecycle.Remaining || s.Remaining == threshold),
                    cancellation,
                    async (db, ct) =>
                        await db.AutomationCountdowns.AnyAsync(
                            t =>
                                t.HostId == host.Id
                                && t.Name == timer.Name
                                && t.OwnerViewerId == timer.OwnerViewerId
                                && t.OccurrenceId == timer.OccurrenceId
                                && t.ProcessId == ProcessId
                                && t.AutomationGeneration == timer.AutomationGeneration
                                && (
                                    kind == CountdownLifecycle.Cancelled
                                        ? t.WasCancelled && !t.IsRunning
                                    : kind == CountdownLifecycle.Started
                                    || kind == CountdownLifecycle.Reset
                                        ? t.IsRunning && !t.WasCancelled
                                    : !t.WasCancelled
                                ),
                            ct
                        ),
                    deferExecution: true
                );
        }
        finally
        {
            if (
                timer.OwnerViewerId.Length > 0
                && kind is CountdownLifecycle.Finished or CountdownLifecycle.Cancelled
            )
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                _ = await db
                    .AutomationCountdowns.Where(t =>
                        t.HostId == timer.HostId
                        && t.OwnerViewerId == timer.OwnerViewerId
                        && t.Name == timer.Name
                        && t.OccurrenceId == timer.OccurrenceId
                        && t.ProcessId == timer.ProcessId
                        && t.AutomationGeneration == timer.AutomationGeneration
                        && !t.IsRunning
                    )
                    .ExecuteDeleteAsync();
            }
        }
    }
}

internal abstract record AutomationCountdownApplyOutcome
{
    private AutomationCountdownApplyOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Started, TResult> started,
        Func<AlreadyRunning, TResult> alreadyRunning,
        Func<Applied, TResult> applied,
        Func<Unavailable, TResult> unavailable
    );

    internal sealed record Started : AutomationCountdownApplyOutcome
    {
        internal override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<AlreadyRunning, TResult> alreadyRunning,
            Func<Applied, TResult> applied,
            Func<Unavailable, TResult> unavailable
        ) => started(this);
    }

    internal sealed record AlreadyRunning : AutomationCountdownApplyOutcome
    {
        internal override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<AlreadyRunning, TResult> alreadyRunning,
            Func<Applied, TResult> applied,
            Func<Unavailable, TResult> unavailable
        ) => alreadyRunning(this);
    }

    internal sealed record Applied : AutomationCountdownApplyOutcome
    {
        internal override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<AlreadyRunning, TResult> alreadyRunning,
            Func<Applied, TResult> applied,
            Func<Unavailable, TResult> unavailable
        ) => applied(this);
    }

    internal sealed record Unavailable : AutomationCountdownApplyOutcome
    {
        internal override TResult Match<TResult>(
            Func<Started, TResult> started,
            Func<AlreadyRunning, TResult> alreadyRunning,
            Func<Applied, TResult> applied,
            Func<Unavailable, TResult> unavailable
        ) => unavailable(this);
    }
}
