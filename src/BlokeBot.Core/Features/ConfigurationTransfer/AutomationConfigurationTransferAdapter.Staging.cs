using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class AutomationConfigurationTransferAdapter
{
    private async Task StageDraftsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<AutomationFlowDraft> drafts,
        SectionImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        var existing = await db
            .AutomationFlows.Include(value => value.Nodes)
            .Include(value => value.Edges)
            .Where(value => value.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        foreach (var draft in drafts)
        {
            var flow = draft.Id is { } id ? existing.Single(value => value.Id == id.Value) : null;
            if (flow is not null && selection.Strategy == ImportConflictStrategy.AddMissing)
            {
                continue;
            }
            if (flow is null)
            {
                flow = new AutomationFlow
                {
                    Id = Guid.NewGuid(),
                    HostId = hostId,
                    CreatedAtUtc = Now(),
                };
                _ = db.AutomationFlows.Add(flow);
            }
            else
            {
                db.AutomationFlowEdges.RemoveRange(flow.Edges);
                db.AutomationFlowNodes.RemoveRange(flow.Nodes);
            }
            flow.Name = draft.Name.Trim();
            flow.SchemaVersion = draft.SchemaVersion;
            flow.IsEnabled = draft.IsEnabled;
            flow.UseVerticalLayout = draft.Canvas.Orientation == AutomationFlowOrientation.Vertical;
            flow.UseSmoothEdges = draft.Canvas.EdgeStyle == AutomationEdgeStyle.Smooth;
            flow.UpdatedAtUtc = Now();
            db.AutomationFlowNodes.AddRange(
                draft.Nodes.Select(node => AutomationFlowService.Persist(flow.Id, node))
            );
            db.AutomationFlowEdges.AddRange(
                draft.Edges.Select(edge => AutomationFlowService.Persist(flow.Id, edge))
            );
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        if (selection.Strategy != ImportConflictStrategy.ReplaceSection)
        {
            return;
        }

        var importedNames = drafts
            .Select(value => ConfigurationImportReferencePlan.NormalizeName(value.Name))
            .ToHashSet();
        foreach (
            var flow in existing.Where(value =>
                !importedNames.Contains(ConfigurationImportReferencePlan.NormalizeName(value.Name))
            )
        )
        {
            var hasRuns = await db.AutomationFlowRuns.AnyAsync(
                value => value.FlowId == flow.Id,
                cancellationToken
            );
            if (hasRuns)
            {
                continue;
            }
            _ = db.AutomationFlows.Remove(flow);
        }
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
