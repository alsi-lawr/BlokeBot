using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class AutomationConfigurationTransferAdapter
{
    private async Task<MappedAutomationSet> BuildDraftsAsync(
        BlokeBotDbContext db,
        int hostId,
        AutomationsSectionV2 section,
        ConfigurationImportReferencePlan references,
        bool allowPlannedCommands,
        ICollection<ConfigurationValidationIssue> issues,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<AutomationSubflowV2> ordered;
        try
        {
            ordered = AutomationPortableDependencies.Order(section.Subflows, section.Flows);
        }
        catch (AutomationConfigurationExportException exception)
        {
            issues.Add(new("sections.automations", exception.Reason));
            return new([], [], []);
        }
        var current = await db
            .AutomationFlows.AsNoTracking()
            .Where(flow => flow.HostId == hostId)
            .Select(flow => new FlowMatch(flow.Id, flow.Name))
            .ToArrayAsync(cancellationToken);
        var commands = await db
            .CustomCommands.AsNoTracking()
            .Where(command => command.HostId == hostId)
            .Select(command => new CommandMatch(command.Id, command.Name))
            .ToArrayAsync(cancellationToken);
        var rewards = await db
            .TwitchCustomRewards.AsNoTracking()
            .Where(reward => reward.HostId == hostId)
            .Select(reward => new RewardMatch(reward.ProviderRewardId, reward.Title))
            .ToArrayAsync(cancellationToken);
        var enabled =
            references.EnabledFeatures
            ?? await db
                .Hosts.Where(host => host.Id == hostId)
                .Select(host => host.EnabledFeatures)
                .SingleAsync(cancellationToken);
        var documentKey = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(section))
        );
        var revisionIds = section
            .Subflows.OrderBy(value => value.Id, StringComparer.Ordinal)
            .Select(
                (value, index) =>
                    (
                        value.Id,
                        Value: new AutomationSubflowRevisionId(
                            DestinationId(hostId, documentKey + "revision", value.Id, index)
                        )
                    )
            )
            .ToDictionary(pair => pair.Id, pair => pair.Value, StringComparer.Ordinal);
        var subflowIds = section
            .Subflows.Select(value => value.SubflowId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(
                (id, index) =>
                    (
                        id,
                        Value: new AutomationSubflowId(
                            DestinationId(hostId, documentKey + "subflow", id, index)
                        )
                    )
            )
            .ToDictionary(pair => pair.id, pair => pair.Value, StringComparer.Ordinal);
        var revisions = new List<AutomationSubflowRevision>();
        foreach (var imported in ordered)
        {
            var graph = await MapGraphAsync(
                imported.Graph,
                null,
                documentKey,
                hostId,
                references,
                commands,
                rewards,
                allowPlannedCommands,
                revisionIds,
                issues,
                cancellationToken
            );
            var validation = await flows.ValidateSubflowTransferAsync(
                new(
                    subflowIds[imported.SubflowId],
                    imported.Description.Trim(),
                    imported.Interface,
                    graph
                ),
                cancellationToken
            );
            AddErrors(imported.Id, validation, issues);
            ValidatePins(graph, revisions, issues);
            var children = AutomationSubflowStore
                .Pins(graph.Nodes)
                .Select(pin => revisions.SingleOrDefault(revision => revision.Id == pin.RevisionId))
                .OfType<AutomationSubflowRevision>()
                .ToArray();
            var required = children.Aggregate(
                AutomationRequiredFeatures.ForDefinitions(
                    graph.Nodes.Select(node => node.Definition.TypeId)
                ),
                (flags, child) => flags | child.RequiredFeatures
            );
            revisions.Add(
                new(
                    revisionIds[imported.Id],
                    subflowIds[imported.SubflowId],
                    imported.Revision,
                    imported.Description.Trim(),
                    imported.Interface,
                    graph,
                    validation.NodeContracts,
                    required,
                    [
                        .. graph
                            .Nodes.Select(node => node.Definition.PluginProvenance)
                            .OfType<AutomationPluginProvenance>()
                            .Concat(children.SelectMany(child => child.PluginDependencies))
                            .Distinct(),
                    ],
                    timeProvider.GetUtcNow()
                )
            );
        }
        var drafts = new List<MappedAutomationDraft>();
        foreach (var imported in section.Flows)
        {
            var matches = current.Where(flow => SameName(flow.Name, imported.Name)).ToArray();
            if (matches.Length > 1)
            {
                issues.Add(
                    new(
                        "sections.automations",
                        $"The destination flow name '{imported.Name}' is ambiguous."
                    )
                );
                continue;
            }
            var id =
                matches.Length == 1
                    ? matches[0].Id
                    : DestinationId(
                        hostId,
                        documentKey + "flow",
                        imported.Id,
                        section.Flows.ToList().IndexOf(imported)
                    );
            if (current.Any(flow => flow.Id == id && !SameName(flow.Name, imported.Name)))
            {
                issues.Add(
                    new(
                        "sections.automations",
                        $"Flow '{imported.Name}' conflicts with a renamed destination flow. Resolve the destination name before importing."
                    )
                );
            }
            var graph = await MapGraphAsync(
                imported,
                new(id),
                documentKey,
                hostId,
                references,
                commands,
                rewards,
                allowPlannedCommands,
                revisionIds,
                issues,
                cancellationToken
            );
            AddErrors(
                imported.Id,
                await flows.ValidateConfigurationTransferAsync(graph, cancellationToken),
                issues
            );
            ValidatePins(graph, revisions, issues);
            var required = AutomationSubflowStore
                .Pins(graph.Nodes)
                .Select(pin => revisions.FirstOrDefault(revision => revision.Id == pin.RevisionId))
                .OfType<AutomationSubflowRevision>()
                .Aggregate(
                    AutomationRequiredFeatures.ForDefinitions(
                        graph.Nodes.Select(node => node.Definition.TypeId)
                    ),
                    (flags, revision) => flags | revision.RequiredFeatures
                );
            if (graph.IsEnabled && (required & ~enabled) != HostFeatureFlags.None)
            {
                issues.Add(
                    new(
                        "sections.automations",
                        $"Enable all required features for flow '{graph.Name}' and its subflows, or import it disabled."
                    )
                );
            }
            drafts.Add(new(imported.Id, graph));
        }
        var fixtures = await MapScenariosAsync(section, drafts, issues, cancellationToken);
        foreach (var revision in revisions)
        {
            if (
                Encoding.UTF8.GetByteCount(AutomationSubflowSerialization.Serialize(revision))
                > 524288
            )
            {
                issues.Add(
                    new("sections.automations.subflows", "Reduce the subflow snapshot size.")
                );
            }
        }
        foreach (var group in fixtures.GroupBy(fixture => fixture.FlowId))
        {
            var names = await db
                .AutomationScenarios.Where(row => row.FlowId == group.Key.Value)
                .Select(row => row.Name)
                .ToArrayAsync(cancellationToken);
            if (
                names.Length + group.Count(fixture => !names.Contains(fixture.Name))
                > AutomationScenarioService.MaximumScenariosPerFlow
            )
            {
                issues.Add(
                    new(
                        "sections.automations.scenarios",
                        "Remove existing scenarios or select fewer scenarios before importing."
                    )
                );
            }
        }
        return new(drafts, revisions, fixtures);
    }

    private static Guid DestinationId(int hostId, string scope, string id, int index)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{hostId}:{scope}:{id}"));
        return new(
            index + 1,
            BitConverter.ToInt16(hash, 0),
            BitConverter.ToInt16(hash, 2),
            hash[4..12]
        );
    }

    private static void ValidatePins(
        AutomationFlowDraft graph,
        IReadOnlyList<AutomationSubflowRevision> revisions,
        ICollection<ConfigurationValidationIssue> issues
    )
    {
        foreach (var node in graph.Nodes)
        {
            if (
                AutomationSubflowDefinitions.TryRead(node.Definition, out var pin)
                && pin.RevisionId is { } id
                && (
                    revisions.FirstOrDefault(revision => revision.Id == id) is not { } revision
                    || !AutomationSubflowDefinitions.SameInterface(
                        pin.Interface,
                        revision.Interface
                    )
                )
            )
            {
                issues.Add(
                    new(
                        "sections.automations",
                        $"Node '{node.Id.Value}' must pin an included revision with the same interface."
                    )
                );
            }
        }
    }

    private static void AddErrors(
        string id,
        AutomationGraphValidation validation,
        ICollection<ConfigurationValidationIssue> issues
    )
    {
        if (validation.Gate is not null)
        {
            issues.Add(
                new("sections.automations", "The destination automation catalog is unavailable.")
            );
        }
        foreach (var error in validation.Errors)
        {
            issues.Add(new($"sections.automations[{id}]", error.Message));
        }
    }

    private sealed record MappedAutomationDraft(string ImportedId, AutomationFlowDraft Draft);

    private sealed record MappedScenario(
        string ImportedId,
        AutomationFlowId FlowId,
        string Name,
        AutomationScenarioFixture Fixture
    );

    private sealed record MappedAutomationSet(
        IReadOnlyList<MappedAutomationDraft> Flows,
        IReadOnlyList<AutomationSubflowRevision> Revisions,
        IReadOnlyList<MappedScenario> Scenarios
    );

    private sealed record FlowMatch(Guid Id, string Name);
}
