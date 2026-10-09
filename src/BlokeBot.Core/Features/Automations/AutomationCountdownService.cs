using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using static BlokeBot.Core.Features.Automations.ExpandedAutomationContext;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationCountdownService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock,
    Func<AutomationRuntimeService> runtime
)
{
    internal Guid ProcessId { get; } = Guid.NewGuid();
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
        _ = await db
            .AutomationCountdowns.Where(t => t.IsRunning && t.ProcessId != ProcessId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.IsRunning, false).SetProperty(t => t.WasCancelled, true)
            );
    }

    internal async Task<bool> ApplyAsync(
        AutomationHostId hostId,
        CountdownActionConfiguration action,
        CancellationToken cancellation
    )
    {
        await InitializeAsync(cancellation);
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
        if (await MainDatabaseStatements.LockHostAsync(db, hostId.Value, cancellation) == 0)
        {
            return false;
        }
        var host = await db
            .Hosts.AsNoTracking()
            .SingleAsync(h => h.Id == hostId.Value, cancellation);
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return false;
        }
        var timer = await db.AutomationCountdowns.SingleOrDefaultAsync(
            t => t.HostId == hostId.Value && t.Name == action.Name,
            cancellation
        );
        var running =
            timer is { IsRunning: true }
            && timer.ProcessId == ProcessId
            && timer.AutomationGeneration == host.AutomationGeneration;
        if (
            (action.Operation == CountdownOperation.Start && running)
            || (action.Operation == CountdownOperation.Cancel && !running)
        )
        {
            return true;
        }
        var now = clock.GetUtcNow();
        timer ??= new() { HostId = hostId.Value, Name = action.Name };
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
        var kind = action.Operation switch
        {
            CountdownOperation.Start => CountdownLifecycle.Started,
            CountdownOperation.Reset => CountdownLifecycle.Reset,
            CountdownOperation.Cancel => CountdownLifecycle.Cancelled,
        };
        _ = await EmitAsync(host, timer, kind, TimeSpan.Zero, now, cancellation);
        return true;
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
                    continue;
                }
                foreach (
                    var threshold in sources
                        .Where(s => s.Name == timer.Name && s.Event == CountdownLifecycle.Remaining)
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
                    emitted.Add((timer, CountdownLifecycle.Finished, TimeSpan.Zero));
                }
            }
            _ = await db.SaveChangesAsync(cancellation);
            await transaction.CommitAsync(cancellation);
        }
        foreach (var e in emitted)
        {
            _ = await EmitAsync(host, e.Timer, e.Event, e.Threshold, now, cancellation);
        }
    }

    private Task<AutomationDispatchOutcome> EmitAsync(
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
                Text("timer-occurrence", timer.OccurrenceId.ToString()),
                Text("event-kind", kind.ToString()),
                Time("deadline", new(timer.DeadlineUtc, TimeSpan.Zero)),
                Number(
                    "remaining-seconds",
                    (decimal)Math.Max(0, (timer.DeadlineUtc - now.UtcDateTime).TotalSeconds)
                ),
            ]
        );
        return runtime()
            .DispatchExpandedAsync(
                context,
                c =>
                    c is CountdownSourceConfiguration s
                    && s.Name == timer.Name
                    && s.Event == kind
                    && (kind != CountdownLifecycle.Remaining || s.Remaining == threshold),
                cancellation,
                async (db, ct) =>
                    await db.AutomationCountdowns.AnyAsync(
                        t =>
                            t.HostId == host.Id
                            && t.Name == timer.Name
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
}
