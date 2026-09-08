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
        MappedAutomationSet mapped,
        SectionImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Configuration staging requires the caller's transaction."
            );
        }
        _ = await MainDatabaseStatements.LockHostAsync(db, hostId, cancellationToken);
        var existing = await db
            .AutomationFlows.Where(flow => flow.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var appliedFlows = mapped
            .Flows.Where(imported =>
                selection.Strategy != ImportConflictStrategy.AddMissing
                || !existing.Any(flow => flow.Id == imported.Draft.Id!.Value.Value)
            )
            .ToArray();
        var needed = appliedFlows
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
        await StageRevisionsAsync(
            db,
            hostId,
            mapped.Revisions.Where(revision => needed.Contains(revision.SubflowId)).ToArray(),
            cancellationToken
        );
        foreach (var imported in appliedFlows)
        {
            var draft = imported.Draft;
            var flow = existing.SingleOrDefault(flow => flow.Id == draft.Id!.Value.Value);
            if (flow is null)
            {
                flow = new()
                {
                    Id = draft.Id!.Value.Value,
                    HostId = hostId,
                    CreatedAtUtc = Now(),
                };
                _ = db.AutomationFlows.Add(flow);
            }
            else
            {
                _ = await db
                    .AutomationFlowEdges.Where(edge => edge.FlowId == flow.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                _ = await db
                    .AutomationFlowNodes.Where(node => node.FlowId == flow.Id)
                    .ExecuteDeleteAsync(cancellationToken);
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
            _ = await db.SaveChangesAsync(cancellationToken);
            db.AutomationSubflowCallers.AddRange(
                AutomationSubflowStore
                    .Calls(draft.Nodes)
                    .Select(pin => new AutomationSubflowCallerReference
                    {
                        HostId = hostId,
                        NodeId = pin.NodeId.Value,
                        SubflowId = pin.SubflowId.Value,
                    })
            );
            await StageScenariosAsync(
                db,
                draft.Id!.Value,
                mapped.Scenarios.Where(scenario => scenario.FlowId == draft.Id.Value).ToArray(),
                cancellationToken
            );
        }
        if (selection.Strategy == ImportConflictStrategy.ReplaceSection)
        {
            var names = mapped
                .Flows.Select(flow =>
                    ConfigurationImportReferencePlan.NormalizeName(flow.Draft.Name)
                )
                .ToHashSet();
            foreach (
                var flow in existing.Where(flow =>
                    !names.Contains(ConfigurationImportReferencePlan.NormalizeName(flow.Name))
                )
            )
            {
                if (
                    !await db.AutomationFlowRuns.AnyAsync(
                        run => run.FlowId == flow.Id,
                        cancellationToken
                    )
                )
                {
                    _ = db.AutomationFlows.Remove(flow);
                }
            }
        }
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private async Task StageRevisionsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<AutomationSubflowRevision> revisions,
        CancellationToken cancellationToken
    )
    {
        foreach (var imported in revisions)
        {
            var revision = imported;
            var parent = await db.AutomationSubflows.SingleOrDefaultAsync(
                row => row.HostId == hostId && row.Id == revision.SubflowId.Value,
                cancellationToken
            );
            if (parent is null)
            {
                parent = new()
                {
                    HostId = hostId,
                    Id = revision.SubflowId.Value,
                    LastRevision = revision.Revision,
                };
                _ = db.AutomationSubflows.Add(parent);
            }
            else
            {
                var stored = await db
                    .AutomationSubflowRevisions.AsNoTracking()
                    .SingleAsync(
                        row =>
                            row.HostId == hostId
                            && row.SubflowId == parent.Id
                            && row.Revision == parent.LastRevision,
                        cancellationToken
                    );
                var current = AutomationSubflowSerialization.Restore(stored.SnapshotJson);
                if (
                    AutomationSubflowSerialization.Serialize(
                        current with
                        {
                            Id = revision.Id,
                            Revision = revision.Revision,
                            PublishedAtUtc = revision.PublishedAtUtc,
                        }
                    ) == AutomationSubflowSerialization.Serialize(revision)
                )
                {
                    continue;
                }
                revision = revision with
                {
                    Id = new(Guid.NewGuid()),
                    Revision = checked(parent.LastRevision + 1),
                };
                parent.LastRevision = revision.Revision;
                _ = await db
                    .AutomationSubflowNestedCallers.Where(row =>
                        row.HostId == hostId && row.CallerSubflowId == parent.Id
                    )
                    .ExecuteDeleteAsync(cancellationToken);
            }
            _ = db.AutomationSubflowRevisions.Add(
                new()
                {
                    HostId = hostId,
                    Id = revision.Id.Value,
                    SubflowId = revision.SubflowId.Value,
                    Revision = revision.Revision,
                    SnapshotJson = AutomationSubflowSerialization.Serialize(revision),
                }
            );
            db.AutomationSubflowNestedCallers.AddRange(
                AutomationSubflowStore
                    .Calls(revision.Graph.Nodes)
                    .Select(pin => new AutomationSubflowNestedCallerReference
                    {
                        HostId = hostId,
                        CallerSubflowId = revision.SubflowId.Value,
                        NodeId = pin.NodeId.Value,
                        SubflowId = pin.SubflowId.Value,
                    })
            );
            _ = await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task StageScenariosAsync(
        BlokeBotDbContext db,
        AutomationFlowId flowId,
        IReadOnlyList<MappedScenario> imported,
        CancellationToken cancellationToken
    )
    {
        var current = await db
            .AutomationScenarios.Where(row => row.FlowId == flowId.Value)
            .ToListAsync(cancellationToken);
        foreach (var scenario in imported)
        {
            var row = current.FirstOrDefault(row => row.Name == scenario.Name);
            if (row is null)
            {
                var slot = Enumerable
                    .Range(0, AutomationScenarioService.MaximumScenariosPerFlow)
                    .Except(current.Select(row => row.Slot))
                    .First();
                row = new()
                {
                    Id = DestinationId(
                        scenario.Fixture.Context.HostId.Value,
                        flowId.Value.ToString(),
                        scenario.ImportedId,
                        slot
                    ),
                    FlowId = flowId.Value,
                    Name = scenario.Name,
                    Slot = slot,
                };
                current.Add(row);
                _ = db.AutomationScenarios.Add(row);
            }
            row.FixtureJson = AutomationScenarioSerialization.Serialize(scenario.Fixture);
        }
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
