using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationRunQueryService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostFeatureService features
)
{
    public async Task<AutomationRunQueryOutcome> ListAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var loaded = await features.Load(hostId.Value).RunAsync(cancellationToken);
        var availability = loaded.Match(
            enabled =>
                enabled.Contains(HostFeatureFlags.Automations)
                    ? AutomationCatalogAvailability.Enabled
                    : AutomationCatalogAvailability.Disabled,
            static () => AutomationCatalogAvailability.HostNotFound
        );
        if (availability == AutomationCatalogAvailability.Disabled)
        {
            return new AutomationRunQueryOutcome.FeatureDisabled();
        }

        if (availability == AutomationCatalogAvailability.HostNotFound)
        {
            return new AutomationRunQueryOutcome.HostNotFound();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db
            .AutomationFlowRuns.AsNoTracking()
            .Include(static value => value.NodeRuns)
            .Where(value => value.HostId == hostId.Value)
            .OrderByDescending(static value => value.StartedAtUtc)
            .ToArrayAsync(cancellationToken);
        return new AutomationRunQueryOutcome.Available(
            runs.Select(static run => Summary(run)).ToImmutableArray()
        );
    }

    private static AutomationRunSummary Summary(AutomationFlowRun run) =>
        new(
            new(run.Id),
            new(run.FlowId),
            (AutomationFlowRunState)run.Status,
            new DateTimeOffset(run.StartedAtUtc, TimeSpan.Zero),
            run.CompletedAtUtc is { } completed
                ? new DateTimeOffset(completed, TimeSpan.Zero)
                : null,
            run.NodeRuns.OrderBy(static value => value.Sequence)
                .Select(static node => new AutomationNodeRunSummary(
                    new(node.NodeId),
                    (AutomationNodeRunState)node.Status,
                    node.OutcomeCode,
                    node.CompletedAtUtc is { } completed
                        ? new DateTimeOffset(completed, TimeSpan.Zero)
                        : null,
                    Diagnostics(node.OutputJson)
                ))
                .ToImmutableArray()
        );

    private static ImmutableArray<AutomationValueDiagnostic> Diagnostics(string? outputJson) =>
        outputJson is not null
        && AutomationDataValueSerialization.RestoreOutputs(outputJson)
            is AutomationOutputRestoreOutcome.Available restored
            ? AutomationDataValueSerialization.Diagnostics(restored.Outputs)
            : [];
}
