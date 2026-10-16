using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class AutomationConfigurationTransferAdapter
{
    private async Task<AutomationImportReview> ReviewEffectsAsync(
        BlokeBotDbContext db,
        int hostId,
        MappedAutomationSet mapped,
        SectionImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        var existing = await db
            .AutomationFlows.AsNoTracking()
            .Where(flow => flow.HostId == hostId)
            .Select(flow => new
            {
                flow.Id,
                flow.Name,
                flow.IsEnabled,
                HasRuns = db.AutomationFlowRuns.Any(run => run.FlowId == flow.Id),
            })
            .ToArrayAsync(cancellationToken);
        var applied = mapped
            .Flows.Where(imported =>
                selection.Strategy != ImportConflictStrategy.AddMissing
                || existing.All(flow => flow.Id != imported.Draft.Id!.Value.Value)
            )
            .ToArray();
        var revisions = NeededRevisions(mapped, applied);
        var removed =
            selection.Strategy == ImportConflictStrategy.ReplaceSection
                ? existing
                    .Where(flow =>
                        !flow.HasRuns
                        && mapped.Flows.All(imported => !SameName(imported.Draft.Name, flow.Name))
                    )
                    .Select(flow => new AutomationFlowId(flow.Id))
                    .ToHashSet()
                : [];
        var effects = new List<AutomationImportedEffects>();
        foreach (var imported in applied)
        {
            var draft = imported.Draft;
            effects.Add(
                new(
                    draft.Id!.Value,
                    draft.Name,
                    draft.IsEnabled,
                    existing.FirstOrDefault(flow => flow.Id == draft.Id.Value.Value)?.IsEnabled,
                    await flows.EffectsAsync(db, draft, cancellationToken, revisions)
                )
            );
        }
        var parents = await flows.EffectParentsAsync(
            db,
            new(hostId),
            revisions,
            cancellationToken,
            applied.Select(flow => flow.Draft).ToArray(),
            removed
        );
        return new(hostId, effects, parents);
    }

    private static AutomationSubflowRevision[] NeededRevisions(
        MappedAutomationSet mapped,
        IReadOnlyList<MappedAutomationDraft> applied
    )
    {
        var needed = applied
            .SelectMany(imported => AutomationSubflowStore.Calls(imported.Draft.Nodes))
            .Select(pin => pin.SubflowId)
            .ToHashSet();
        foreach (var revision in mapped.Revisions.Reverse())
        {
            if (needed.Contains(revision.SubflowId))
            {
                needed.UnionWith(
                    AutomationSubflowStore.Calls(revision.Graph.Nodes).Select(pin => pin.SubflowId)
                );
            }
        }
        return mapped.Revisions.Where(revision => needed.Contains(revision.SubflowId)).ToArray();
    }
}
