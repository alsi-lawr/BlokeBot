using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class AutomationConfigurationTransferAdapter
{
    private Task<AutomationFlowDraft> MapGraphAsync(
        AutomationFlowV2 imported,
        AutomationFlowId? flowId,
        string documentKey,
        int hostId,
        ConfigurationImportReferencePlan references,
        IReadOnlyList<CommandMatch> commands,
        IReadOnlyList<RewardMatch> rewards,
        bool planned,
        IReadOnlyDictionary<string, AutomationSubflowRevisionId> revisions,
        ICollection<ConfigurationValidationIssue> issues,
        CancellationToken cancellationToken
    )
    {
        var nodeIds = imported
            .Nodes.Select(
                (node, index) =>
                    (
                        node.Id,
                        Value: new AutomationNodeId(
                            DestinationId(
                                hostId,
                                documentKey + imported.Id + "node",
                                node.Id,
                                index
                            )
                        )
                    )
            )
            .ToDictionary(pair => pair.Id, pair => pair.Value, StringComparer.Ordinal);
        var nodes = ImmutableArray.CreateBuilder<AutomationFlowDraftNode>();
        foreach (var node in imported.Nodes)
        {
            var reference = RemapConfiguration(node, references, commands, rewards, planned);
            if (reference.Rejection is not null)
            {
                issues.Add(
                    new(
                        "sections.automations",
                        $"Resolve the channel reference for node '{node.Id}' before importing."
                    )
                );
            }
            var configuration = reference.Configuration;
            if (node.Subflow is { } binding)
            {
                var revision =
                    binding.RevisionId is { } pin && revisions.TryGetValue(pin, out var resolved)
                        ? resolved
                        : (AutomationSubflowRevisionId?)null;
                if (node.DefinitionId == AutomationSubflowDefinitions.Invoke && revision is null)
                {
                    issues.Add(
                        new(
                            "sections.automations",
                            $"Include pinned revision '{binding.RevisionId}'."
                        )
                    );
                }
                configuration = JsonSerializer.SerializeToElement(
                    new
                    {
                        RevisionId = revision,
                        binding.Interface,
                        FixedInputs = binding.FixedInputsJson,
                    },
                    AutomationSubflowDefinitions.JsonOptions
                );
            }
            AutomationPluginProvenance? provenance = null;
            if (node.Plugin is { } plugin)
            {
                if (
                    !catalog.TryResolvePlugin(
                        new(hostId),
                        new(node.DefinitionId),
                        out var definition
                    )
                    || !AutomationPluginContractV2.Available(definition)
                    || definition.Descriptor.PluginProvenance is not { } current
                    || AutomationPluginContractV2.From(current) != plugin
                )
                {
                    issues.Add(
                        new(
                            "sections.automations",
                            $"Install and enable the compatible existing plugin contract for '{node.DefinitionId}' before importing."
                        )
                    );
                }
                else
                {
                    provenance = current;
                }
            }
            var persisted = new PersistedAutomationNodeDefinition(
                node.DefinitionId,
                node.DefinitionSchemaVersion,
                configuration,
                provenance
            );
            var check = catalog.ValidatePersistedDefinition(persisted);
            if (check is not AutomationConfigurationCheck.Valid valid)
            {
                issues.Add(
                    new(
                        "sections.automations",
                        $"Node '{node.Id}' requires an available compatible schema and plugin contract."
                    )
                );
            }
            else if (
                (
                    AutomationPortableConfiguration.Rejection(valid)
                    ?? AutomationPortableConfiguration.Rejection(node.DefinitionId, configuration)
                ) is
                { } rejection
            )
            {
                issues.Add(new("sections.automations", rejection));
            }
            nodes.Add(
                new(
                    nodeIds[node.Id],
                    persisted,
                    new(node.ExpressionLanguageVersion),
                    node.FailurePolicy,
                    node.InputBindings.ToImmutableDictionary(
                        binding => new AutomationConfigurationFieldId(binding.FieldId),
                        binding => new AutomationInputBinding(
                            binding.Mode,
                            binding.Expression is { } expression
                            && binding.ExpressionLanguageVersion is { } version
                                ? new(new(version), expression)
                                : null
                        )
                    ),
                    new(new(node.CanvasX), new(node.CanvasY)),
                    node.DisplayAlias
                )
            );
        }
        return Task.FromResult(
            new AutomationFlowDraft(
                flowId,
                new(hostId),
                imported.Name.Trim(),
                imported.SchemaVersion,
                imported.Enabled,
                nodes.ToImmutable(),
                [
                    .. imported.Edges.Select(
                        (edge, index) =>
                            new AutomationFlowDraftEdge(
                                DestinationId(
                                    hostId,
                                    documentKey + imported.Id + "edge",
                                    edge.Id,
                                    index
                                ),
                                edge.Kind,
                                nodeIds[edge.SourceNodeId],
                                new(edge.SourcePortId),
                                nodeIds[edge.TargetNodeId],
                                new(edge.TargetPortId)
                            )
                    ),
                ],
                new(imported.Orientation, imported.EdgeStyle)
            )
        );
    }

    private async Task<IReadOnlyList<MappedScenario>> MapScenariosAsync(
        AutomationsSectionV2 section,
        IReadOnlyList<MappedAutomationDraft> drafts,
        ICollection<ConfigurationValidationIssue> issues,
        CancellationToken cancellationToken
    )
    {
        var result = new List<MappedScenario>();
        foreach (var scenario in section.Scenarios)
        {
            var mapped = drafts.FirstOrDefault(draft => draft.ImportedId == scenario.FlowId);
            if (mapped is null)
            {
                issues.Add(
                    new("sections.automations.scenarios", "Select the scenario's owning flow.")
                );
                continue;
            }
            var imported = section.Flows.Single(flow => flow.Id == scenario.FlowId);
            var ids = imported
                .Nodes.Select((node, index) => (node.Id, mapped.Draft.Nodes[index].Id))
                .ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal);
            if (
                !ids.TryGetValue(scenario.SourceNodeId, out var source)
                || scenario.Inputs.Any(input => !ids.ContainsKey(input.NodeId))
                || scenario.Effects.Any(effect => !ids.ContainsKey(effect.NodeId))
            )
            {
                issues.Add(
                    new(
                        "sections.automations.scenarios",
                        "Scenario references must belong to its selected flow."
                    )
                );
                continue;
            }
            try
            {
                var fixture = scenarios.CreatePortableFixture(
                    mapped.Draft,
                    source,
                    scenario.Seed,
                    scenario.ClockUtc,
                    [
                        .. scenario.Inputs.Select(input => new AutomationScenarioGeneratedInput(
                            ids[input.NodeId],
                            new(input.PortId),
                            input.ValueType,
                            input.Nullability,
                            input.Sensitivity,
                            input.Provenance
                        )),
                    ],
                    [
                        .. scenario.Effects.Select(effect => new AutomationScenarioEffect(
                            ids[effect.NodeId],
                            effect.Result
                        )),
                    ]
                );
                if (
                    fixture.SourceDefinitionId.Value != scenario.SourceDefinitionId
                    || fixture.SourceSchemaVersion.Value != scenario.SourceSchemaVersion
                )
                {
                    issues.Add(
                        new(
                            "sections.automations.scenarios",
                            "Recreate this scenario against the current source schema."
                        )
                    );
                }
                AddErrors(
                    scenario.Id,
                    await scenarios.ValidatePortableAsync(
                        mapped.Draft,
                        fixture,
                        cancellationToken,
                        transfer: true
                    ),
                    issues
                );
                result.Add(new(scenario.Id, mapped.Draft.Id!.Value, scenario.Name.Trim(), fixture));
            }
            catch (ArgumentException)
            {
                issues.Add(
                    new("sections.automations.scenarios", "Use safe generated typed recipe inputs.")
                );
            }
        }
        return result;
    }
}
