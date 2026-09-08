using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationExportMappers
{
    internal static async Task<AutomationsSectionV2> AutomationsAsync(
        BlokeBotDbContext db,
        int hostId,
        ConfigurationExportReferencePlan references,
        AutomationCatalogService catalog,
        AutomationFlowService flowService,
        AutomationScenarioService scenarios,
        ConfigurationExportSelection selection,
        CancellationToken cancellationToken
    )
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(cancellationToken);
        _ = await MainDatabaseStatements.LockHostAsync(db, hostId, cancellationToken);
        var rows = await db
            .AutomationFlows.AsNoTracking()
            .AsSplitQuery()
            .Include(flow => flow.Nodes)
            .Include(flow => flow.Edges)
            .Where(flow => flow.HostId == hostId && selection.AutomationFlowIds.Contains(flow.Id))
            .OrderBy(flow => flow.Name)
            .ThenBy(flow => flow.Id)
            .ToArrayAsync(cancellationToken);
        rows = rows.OrderBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Id)
            .ToArray();
        if (rows.Length != selection.AutomationFlowIds.Count)
        {
            throw new AutomationConfigurationExportException(
                "graph",
                "Select existing flows owned by this channel."
            );
        }
        var drafts = rows.Select(flow =>
                AutomationFlowService.RestoreDraft(flow)
                    is AutomationFlowDraftRestoreOutcome.Available restored
                    ? restored.Draft
                    : throw new AutomationConfigurationExportException(
                        "graph",
                        "Repair the invalid persisted flow before exporting."
                    )
            )
            .ToArray();
        var loaded = await AutomationSubflowStore.LoadClosureAsync(
            db,
            new(hostId),
            drafts
                .SelectMany(draft => AutomationSubflowStore.Calls(draft.Nodes))
                .Select(pin => pin.SubflowId),
            null,
            cancellationToken
        );
        if (loaded is AutomationSubflowClosureOutcome.Invalid invalid)
        {
            throw new AutomationConfigurationExportException(
                "subflow",
                string.Join(" ", invalid.Errors.Select(error => error.Message))
            );
        }
        var closure = ((AutomationSubflowClosureOutcome.Available)loaded).Closure;
        var callErrors = drafts
            .SelectMany(draft => AutomationSubflowStore.ValidateCalls(draft, closure))
            .ToArray();
        if (callErrors.Length > 0)
        {
            throw new AutomationConfigurationExportException(
                "subflow",
                string.Join(" ", callErrors.Select(error => error.Message))
            );
        }
        var revisions = ((AutomationSubflowClosureOutcome.Available)loaded)
            .Closure.Revisions.OrderBy(revision => revision.SubflowId.Value)
            .ThenBy(revision => revision.Revision)
            .ToArray();
        var subflowIds = revisions
            .Select(revision => revision.SubflowId)
            .Distinct()
            .Select((id, index) => (id, Label("subflow", index)))
            .ToDictionary(pair => pair.id, pair => pair.Item2);
        var hostReferences = new Dictionary<string, AutomationHostReferenceV2>(
            StringComparer.Ordinal
        );
        var exported = new List<AutomationFlowV2>();
        var nodeMaps = new Dictionary<AutomationFlowId, Dictionary<AutomationNodeId, string>>();
        foreach (var draft in drafts)
        {
            var validation = await flowService.ValidatePreparedAsync(
                draft,
                closure,
                AutomationFlowService.AutomationGraphAdmission.ConfigurationTransfer,
                cancellationToken,
                db
            );
            if (!validation.Errors.IsEmpty)
            {
                throw new AutomationConfigurationExportException(
                    "graph",
                    string.Join(" ", validation.Errors.Select(error => error.Message))
                );
            }
            var graph = ExportGraph(
                draft,
                Label("flow", exported.Count),
                subflowIds,
                references,
                hostReferences,
                catalog
            );
            exported.Add(graph);
            nodeMaps.Add(
                draft.Id!.Value,
                draft
                    .Nodes.OrderBy(node => node.Id.Value)
                    .Select((node, index) => (node.Id, Label("node", index)))
                    .ToDictionary(pair => pair.Id, pair => pair.Item2)
            );
        }
        var subflows = revisions
            .Select(revision => new AutomationSubflowV2(
                subflowIds[revision.SubflowId],
                revision.Description,
                revision.Interface,
                ExportGraph(
                    revision.Graph,
                    subflowIds[revision.SubflowId],
                    subflowIds,
                    references,
                    hostReferences,
                    catalog
                )
            ))
            .ToArray();
        var ordered = AutomationPortableDependencies.Order(subflows, exported);
        var scenarioRows = await db
            .AutomationScenarios.AsNoTracking()
            .Where(row =>
                selection.AutomationScenarioIds.Contains(row.Id)
                && db.AutomationFlows.Any(flow => flow.Id == row.FlowId && flow.HostId == hostId)
            )
            .OrderBy(row => row.FlowId)
            .ThenBy(row => row.Slot)
            .ToArrayAsync(cancellationToken);
        if (
            scenarioRows.Length != selection.AutomationScenarioIds.Count
            || scenarioRows.Any(row => !selection.AutomationFlowIds.Contains(row.FlowId))
        )
        {
            throw new AutomationConfigurationExportException(
                "scenario",
                "Select scenarios belonging to the selected channel flows."
            );
        }
        var flowOrder = drafts
            .Select((draft, index) => (Id: draft.Id!.Value.Value, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
        scenarioRows = scenarioRows
            .OrderBy(row => flowOrder[row.FlowId])
            .ThenBy(row => row.Slot)
            .ToArray();
        var portable = new List<AutomationScenarioV2>();
        foreach (var row in scenarioRows)
        {
            var draft = drafts.Single(draft => draft.Id!.Value.Value == row.FlowId);
            var fixture = AutomationScenarioSerialization.Deserialize(row.FixtureJson);
            if (!scenarios.IsPortableFixture(draft, fixture))
            {
                throw new AutomationConfigurationExportException(
                    "scenario",
                    $"Scenario '{row.Name}' contains custom or unattested values. Recreate it with generated portable inputs, or remove it from this export selection."
                );
            }
            var validation = await scenarios.ValidatePortableAsync(
                draft,
                fixture,
                cancellationToken,
                resolved: closure,
                preparationDb: db
            );
            if (!validation.Errors.IsEmpty || validation.Gate is not null)
            {
                throw new AutomationConfigurationExportException(
                    "scenario",
                    "Recreate the scenario against the current source and input schemas before exporting."
                );
            }
            var map = nodeMaps[draft.Id!.Value];
            portable.Add(
                new(
                    Label("scenario", portable.Count),
                    exported[Array.IndexOf(drafts, draft)].Id,
                    row.Name,
                    map[fixture.SourceNodeId],
                    fixture.SourceDefinitionId.Value,
                    fixture.SourceSchemaVersion.Value,
                    fixture.ClockUtc,
                    fixture.Seed,
                    fixture
                        .SyntheticRecipe!.Inputs.Select(input => new AutomationGeneratedInputV2(
                            map[input.NodeId],
                            input.PortId.Value,
                            input.ValueType,
                            input.Nullability,
                            input.Sensitivity,
                            input.Provenance
                        ))
                        .ToArray(),
                    fixture
                        .Effects.Select(effect => new AutomationScenarioEffectV2(
                            map[effect.NodeId],
                            effect.Result
                        ))
                        .ToArray()
                )
            );
        }
        return new(
            exported,
            hostReferences.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray()
        )
        {
            Subflows = ordered,
            Scenarios = portable,
        };
    }

    private static string Label(string prefix, int index) => $"{prefix}-{index + 1:D4}";
}
