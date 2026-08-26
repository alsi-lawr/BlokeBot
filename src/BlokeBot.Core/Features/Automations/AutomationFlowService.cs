using System.Collections.Immutable;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationCatalogService catalog,
    AutomationExpressionService expressions,
    IOverlayCueAdmissionService overlayCues,
    TimeProvider clock,
    IEventSubChannelReconciliationTrigger? eventSub = null
)
{
    public async Task<AutomationFlowQueryOutcome> ListAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var availability = await catalog.DiscoverAsync(hostId, cancellationToken);
        if (availability.Availability == AutomationCatalogAvailability.Disabled)
        {
            return new AutomationFlowQueryOutcome.FeatureDisabled();
        }

        if (availability.Availability == AutomationCatalogAvailability.HostNotFound)
        {
            return new AutomationFlowQueryOutcome.HostNotFound();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var flows = await db
            .AutomationFlows.AsNoTracking()
            .Include(static value => value.Nodes)
            .Include(static value => value.Edges)
            .Where(value => value.HostId == hostId.Value)
            .OrderByDescending(static value => value.UpdatedAtUtc)
            .ThenBy(static value => value.Name)
            .ToArrayAsync(cancellationToken);
        var snapshots = ImmutableArray.CreateBuilder<AutomationFlowSnapshot>();
        foreach (var flow in flows)
        {
            if (RestoreDraft(flow) is not AutomationFlowDraftRestoreOutcome.Available available)
            {
                return new AutomationFlowQueryOutcome.Invalid(
                    new(flow.Id),
                    [MalformedGraphError()]
                );
            }

            snapshots.Add(
                new(
                    available.Draft,
                    new DateTimeOffset(flow.CreatedAtUtc, TimeSpan.Zero),
                    new DateTimeOffset(flow.UpdatedAtUtc, TimeSpan.Zero),
                    flow.UnavailableReason
                )
            );
        }

        return new AutomationFlowQueryOutcome.Available(snapshots.ToImmutable());
    }

    public async Task<AutomationFlowValidationOutcome> ValidateDraftAsync(
        AutomationFlowDraft draft,
        CancellationToken cancellationToken
    )
    {
        var validation = await ValidateAsync(draft, cancellationToken);
        return validation.Gate switch
        {
            AutomationCatalogAvailability.Disabled =>
                new AutomationFlowValidationOutcome.FeatureDisabled(),
            AutomationCatalogAvailability.HostNotFound =>
                new AutomationFlowValidationOutcome.HostNotFound(),
            null when validation.Errors.IsEmpty => new AutomationFlowValidationOutcome.Valid(),
            null => new AutomationFlowValidationOutcome.Invalid(validation.Errors),
            _ => throw new InvalidOperationException("Unexpected automation catalog state."),
        };
    }

    public async Task<AutomationFlowDeleteOutcome> DeleteAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        CancellationToken cancellationToken
    )
    {
        var availability = await catalog.DiscoverAsync(hostId, cancellationToken);
        if (availability.Availability == AutomationCatalogAvailability.Disabled)
        {
            return new AutomationFlowDeleteOutcome.FeatureDisabled();
        }

        if (availability.Availability == AutomationCatalogAvailability.HostNotFound)
        {
            return new AutomationFlowDeleteOutcome.HostNotFound();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db
            .AutomationFlows.Where(value =>
                value.Id == flowId.Value && value.HostId == hostId.Value
            )
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return new AutomationFlowDeleteOutcome.FlowNotFound();
        }

        await ReconcileEventSubAsync(cancellationToken);
        return new AutomationFlowDeleteOutcome.Deleted();
    }

    public async Task<AutomationFlowDuplicateOutcome> DuplicateAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        CancellationToken cancellationToken
    )
    {
        var availability = await catalog.DiscoverAsync(hostId, cancellationToken);
        if (availability.Availability == AutomationCatalogAvailability.Disabled)
        {
            return new AutomationFlowDuplicateOutcome.FeatureDisabled();
        }

        if (availability.Availability == AutomationCatalogAvailability.HostNotFound)
        {
            return new AutomationFlowDuplicateOutcome.HostNotFound();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var flow = await db
            .AutomationFlows.AsNoTracking()
            .Include(static value => value.Nodes)
            .Include(static value => value.Edges)
            .SingleOrDefaultAsync(
                value => value.Id == flowId.Value && value.HostId == hostId.Value,
                cancellationToken
            );
        if (flow is null)
        {
            return new AutomationFlowDuplicateOutcome.FlowNotFound();
        }

        if (RestoreDraft(flow) is not AutomationFlowDraftRestoreOutcome.Available restored)
        {
            return new AutomationFlowDuplicateOutcome.Invalid([MalformedGraphError()]);
        }

        var original = restored.Draft;
        var nodeIds = original.Nodes.ToDictionary(
            static node => node.Id,
            static _ => new AutomationNodeId(Guid.NewGuid())
        );
        var duplicate = original with
        {
            Id = null,
            Name = DuplicateName(original.Name),
            IsEnabled = false,
            Nodes = original
                .Nodes.Select(node => node with { Id = nodeIds[node.Id] })
                .ToImmutableArray(),
            Edges = original
                .Edges.Select(edge =>
                    edge with
                    {
                        Id = Guid.NewGuid(),
                        SourceNodeId = nodeIds[edge.SourceNodeId],
                        TargetNodeId = nodeIds[edge.TargetNodeId],
                    }
                )
                .ToImmutableArray(),
        };
        return await SaveAsync(duplicate, cancellationToken) switch
        {
            AutomationFlowSaveOutcome.Saved saved => new AutomationFlowDuplicateOutcome.Duplicated(
                saved.FlowId
            ),
            AutomationFlowSaveOutcome.Invalid invalid => new AutomationFlowDuplicateOutcome.Invalid(
                invalid.Errors
            ),
            AutomationFlowSaveOutcome.FeatureDisabled =>
                new AutomationFlowDuplicateOutcome.FeatureDisabled(),
            AutomationFlowSaveOutcome.HostNotFound =>
                new AutomationFlowDuplicateOutcome.HostNotFound(),
            AutomationFlowSaveOutcome.FlowNotFound =>
                new AutomationFlowDuplicateOutcome.FlowNotFound(),
            _ => throw new InvalidOperationException("Unknown automation duplicate outcome."),
        };
    }

    private enum AutomationGraphAdmission
    {
        Saved,
        Frozen,
        ConfigurationTransfer,
    }
}

internal sealed record AutomationGraphValidation(
    AutomationCatalogAvailability? Gate,
    ImmutableArray<AutomationGraphError> Errors
);

internal abstract record AutomationFlowDraftRestoreOutcome
{
    private AutomationFlowDraftRestoreOutcome() { }

    internal sealed record Available(AutomationFlowDraft Draft) : AutomationFlowDraftRestoreOutcome;

    internal sealed record Invalid : AutomationFlowDraftRestoreOutcome;
}
