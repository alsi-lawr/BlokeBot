using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationCountdownControlService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationCountdownService countdowns,
    AutomationCatalogService catalog
)
{
    public async Task<AutomationCountdownControlOutcome> ApplyAsync(
        AutomationHostId hostId,
        CountdownActionConfiguration action,
        CancellationToken ct
    )
    {
        if (!Enum.IsDefined(action.Operation))
        {
            return new AutomationCountdownControlOutcome.Unavailable();
        }
        var definition = action.Operation switch
        {
            CountdownOperation.Start => AutomationDefinitionIds.StartCountdownAction,
            CountdownOperation.Reset => AutomationDefinitionIds.ResetCountdownAction,
            CountdownOperation.Cancel => AutomationDefinitionIds.CancelCountdownAction,
        };
        return
            await catalog.ValidateForSaveAsync(hostId, definition, new(1), action, ct)
                is not AutomationConfigurationCheck.Valid
            ? new AutomationCountdownControlOutcome.Unavailable()
            : await (await countdowns.ApplyAsync(hostId, action, ct)).Match(
                _ => ReadAsync(hostId, action.Name, ct),
                _ => ReadAsync(hostId, action.Name, ct),
                _ => ReadAsync(hostId, action.Name, ct),
                static _ =>
                    Task.FromResult<AutomationCountdownControlOutcome>(
                        new AutomationCountdownControlOutcome.Unavailable()
                    )
            );
    }

    internal async Task<AutomationCountdownControlOutcome> ApplyOwnedAsync(
        AutomationHostId host,
        AutomationCountdownSnapshot target,
        CountdownOperation operation,
        TimeSpan duration,
        CancellationToken ct
    )
    {
        if (
            target.OwnerViewerId.Length == 0
            || operation == CountdownOperation.Start
            || target.OccurrenceId is null
        )
        {
            return new AutomationCountdownControlOutcome.Unavailable();
        }
        var action = new CountdownActionConfiguration(
            target.Name,
            operation == CountdownOperation.Cancel ? TimeSpan.Zero : duration,
            operation,
            CountdownTimerOwner.Viewer
        );
        var definition =
            operation == CountdownOperation.Reset
                ? AutomationDefinitionIds.ResetCountdownAction
                : AutomationDefinitionIds.CancelCountdownAction;
        if (
            await catalog.ValidateForSaveAsync(host, definition, new(1), action, ct)
            is not AutomationConfigurationCheck.Valid
        )
        {
            return new AutomationCountdownControlOutcome.Unavailable();
        }
        var applied = await countdowns.ApplyPreparedAsync(
            host,
            new AutomationPreparedCountdown(
                action,
                target.OwnerViewerId,
                target.OwnerDisplayName,
                target.OccurrenceId
            ),
            ct
        );
        return await applied.Match(
            _ => ReadAsync(host, target.Name, ct, target.OwnerViewerId),
            _ => ReadAsync(host, target.Name, ct, target.OwnerViewerId),
            _ => ReadAsync(host, target.Name, ct, target.OwnerViewerId),
            _ =>
                Task.FromResult<AutomationCountdownControlOutcome>(
                    new AutomationCountdownControlOutcome.Unavailable()
                )
        );
    }

    internal async Task<ImmutableArray<AutomationCountdownSnapshot>> ListOwnedAsync(
        AutomationHostId host,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var generation = await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Id == host.Value
                && (value.EnabledFeatures & HostFeatureFlags.Automations) != 0
            )
            .Select(value => (int?)value.AutomationGeneration)
            .SingleOrDefaultAsync(ct);
        if (generation is null)
        {
            return [];
        }
        var timers = await db
            .AutomationCountdowns.AsNoTracking()
            .Where(timer =>
                timer.HostId == host.Value
                && timer.OwnerViewerId != ""
                && timer.IsRunning
                && timer.ProcessId == countdowns.ProcessId
                && timer.AutomationGeneration == generation
            )
            .OrderBy(timer => timer.Name)
            .ThenBy(timer => timer.OwnerViewerId)
            .ToArrayAsync(ct);
        return timers
            .Select(timer => new AutomationCountdownSnapshot(
                timer.Name,
                AutomationCountdownState.Running,
                timer.OccurrenceId,
                new(timer.StartedAtUtc, TimeSpan.Zero),
                new(timer.DeadlineUtc, TimeSpan.Zero),
                timer.OwnerViewerId,
                timer.OwnerDisplayName
            ))
            .ToImmutableArray();
    }

    public async Task<AutomationCountdownControlOutcome> ReadAsync(
        AutomationHostId hostId,
        string name,
        CancellationToken ct,
        string ownerViewerId = ""
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == hostId.Value)
            .Select(host => new { host.EnabledFeatures, host.AutomationGeneration })
            .SingleOrDefaultAsync(ct);
        if (host is null || !host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return new AutomationCountdownControlOutcome.Unavailable();
        }
        var timer = await db
            .AutomationCountdowns.AsNoTracking()
            .SingleOrDefaultAsync(
                timer =>
                    timer.HostId == hostId.Value
                    && timer.Name == name
                    && timer.OwnerViewerId == ownerViewerId,
                ct
            );
        if (timer is null)
        {
            return new AutomationCountdownControlOutcome.Available(
                new(name, AutomationCountdownState.NotStarted, null, null, null, ownerViewerId)
            );
        }
        var current =
            timer.ProcessId == countdowns.ProcessId
            && timer.AutomationGeneration == host.AutomationGeneration;
        var state =
            timer.WasCancelled || (timer.IsRunning && !current) ? AutomationCountdownState.Cancelled
            : timer.IsRunning ? AutomationCountdownState.Running
            : AutomationCountdownState.Finished;
        return new AutomationCountdownControlOutcome.Available(
            new(
                name,
                state,
                timer.OccurrenceId,
                new(timer.StartedAtUtc, TimeSpan.Zero),
                new(timer.DeadlineUtc, TimeSpan.Zero),
                timer.OwnerViewerId,
                timer.OwnerDisplayName
            )
        );
    }
}
