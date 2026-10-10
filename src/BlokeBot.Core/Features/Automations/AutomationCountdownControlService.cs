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
                is AutomationConfigurationCheck.Valid
            && await countdowns.ApplyAsync(hostId, action, ct)
            ? await ReadAsync(hostId, action.Name, ct)
            : new AutomationCountdownControlOutcome.Unavailable();
    }

    public async Task<AutomationCountdownControlOutcome> ReadAsync(
        AutomationHostId hostId,
        string name,
        CancellationToken ct
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
            .SingleOrDefaultAsync(timer => timer.HostId == hostId.Value && timer.Name == name, ct);
        if (timer is null)
        {
            return new AutomationCountdownControlOutcome.Available(
                new(name, AutomationCountdownState.NotStarted, null, null, null)
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
                new(timer.DeadlineUtc, TimeSpan.Zero)
            )
        );
    }
}
