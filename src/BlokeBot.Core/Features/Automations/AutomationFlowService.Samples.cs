using System.Collections.Immutable;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    public async Task<AutomationSampleRunOutcome> RunSampleAsync(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        CancellationToken cancellationToken
    ) => await RunSampleAsync(draft, sourceNodeId, 0, cancellationToken);

    internal async Task<AutomationSampleRunOutcome> RunSampleAsync(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        ulong seed,
        CancellationToken cancellationToken
    )
    {
        var validation = await ValidateAsync(draft, cancellationToken);
        if (validation.Gate == AutomationCatalogAvailability.Disabled)
        {
            return new AutomationSampleRunOutcome.FeatureDisabled();
        }

        if (validation.Gate == AutomationCatalogAvailability.HostNotFound)
        {
            return new AutomationSampleRunOutcome.HostNotFound();
        }

        if (!validation.Errors.IsEmpty)
        {
            return new AutomationSampleRunOutcome.Invalid(validation.Errors);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleAsync(value => value.Id == draft.HostId.Value, cancellationToken);
        var sourceDefinitionId = draft
            .Nodes.FirstOrDefault(node => node.Id == sourceNodeId)
            ?.Definition.TypeId;
        return await EvaluateSampleAsync(
            draft,
            sourceNodeId,
            SampleContext(
                host,
                sourceDefinitionId ?? AutomationDefinitionIds.IncomingRaidSource.Value
            ),
            seed,
            cancellationToken
        );
    }

    private async Task<AutomationSampleRunOutcome> EvaluateSampleAsync(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        AutomationContext context,
        ulong seed,
        CancellationToken cancellationToken
    )
    {
        var source = draft.Nodes.FirstOrDefault(node => node.Id == sourceNodeId);
        if (
            source is null
            || catalog.ValidatePersistedDefinition(source.Definition)
                is not AutomationConfigurationCheck.Valid
                {
                    Definition.Kind: AutomationNodeKind.Source,
                }
        )
        {
            return new AutomationSampleRunOutcome.Invalid([
                new(sourceNodeId, "sample-source-invalid", "Select a trigger node for the sample."),
            ]);
        }
        var outcomes = ImmutableArray.CreateBuilder<AutomationSampleNodeOutcome>();
        outcomes.Add(new(source.Id, AutomationNodeRunState.Succeeded, "source-received"));
        var persisted = SampleFlow(draft);
        var checkpoints = new AutomationSampleCheckpointStore();
        var integerEntropy = new AutomationSeededIntegerEntropy(seed);
        var pending = new Queue<AutomationNodeId>(Outgoing(draft.Edges, source.Id, null));
        var visited = new HashSet<AutomationNodeId> { source.Id };
        while (pending.TryDequeue(out var nodeId))
        {
            if (!visited.Add(nodeId))
            {
                continue;
            }

            var node = draft.Nodes.Single(candidate => candidate.Id == nodeId);
            var check = catalog.ValidatePersistedDefinition(node.Definition);
            if (check is not AutomationConfigurationCheck.Valid valid)
            {
                outcomes.Add(new(node.Id, AutomationNodeRunState.Failed, "configuration-invalid"));
                return new AutomationSampleRunOutcome.Failed(outcomes.ToImmutable());
            }

            var persistedNode = persisted.Nodes.Single(candidate => candidate.Id == node.Id.Value);
            var inputs = await catalog.Data.ResolveSampleInputsAsync(
                draft.HostId,
                context,
                persisted,
                persistedNode,
                checkpoints,
                integerEntropy,
                cancellationToken
            );
            if (inputs is not AutomationInputResolution.Available resolvedInputs)
            {
                outcomes.Add(
                    new(node.Id, AutomationNodeRunState.Failed, "input-resolution-failed")
                );
                return new AutomationSampleRunOutcome.Failed(outcomes.ToImmutable());
            }

            var evaluated = EvaluateSampleNode(valid.Configuration, resolvedInputs.FieldValues);
            outcomes.Add(
                new(
                    node.Id,
                    evaluated.State,
                    evaluated.OutcomeCode,
                    AutomationDataValueSerialization.Diagnostics(
                        resolvedInputs.FieldValues.ToDictionary(
                            static pair => new AutomationPortId(pair.Key.Value),
                            static pair => pair.Value
                        )
                    )
                )
            );
            if (evaluated.State == AutomationNodeRunState.Failed)
            {
                return new AutomationSampleRunOutcome.Failed(outcomes.ToImmutable());
            }

            foreach (var target in Outgoing(draft.Edges, node.Id, evaluated.SourcePort))
            {
                pending.Enqueue(target);
            }
        }

        return new AutomationSampleRunOutcome.Completed(outcomes.ToImmutable());
    }

    private static AutomationRuntimeSerialization.PersistedFlow SampleFlow(
        AutomationFlowDraft draft
    ) =>
        new(
            draft.Id?.Value ?? Guid.Empty,
            draft.HostId.Value,
            draft.SchemaVersion,
            draft
                .Nodes.Select(static node => new AutomationRuntimeSerialization.PersistedNode(
                    node.Id.Value,
                    node.Definition.TypeId,
                    node.Definition.SchemaVersion,
                    node.Definition.Configuration.GetRawText(),
                    AutomationRuntimeSerialization.SerializeInputBindings(node.InputBindings),
                    node.ExpressionLanguageVersion.Value,
                    node.FailurePolicy == AutomationNodeFailurePolicy.Continue
                ))
                .ToImmutableArray(),
            draft
                .Edges.Select(static edge => new AutomationRuntimeSerialization.PersistedEdge(
                    edge.Id,
                    edge.Kind,
                    edge.SourceNodeId.Value,
                    edge.SourcePortId.Value,
                    edge.TargetNodeId.Value,
                    edge.TargetPortId.Value
                ))
                .ToImmutableArray()
        );

    private SampleNodeEvaluation EvaluateSampleNode(
        AutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs
    ) =>
        configuration switch
        {
            ConditionControlConfiguration => EvaluateSampleCondition(inputs),
            DelayControlConfiguration => new(
                AutomationNodeRunState.Succeeded,
                "delay-skipped",
                "complete"
            ),
            SendChatActionConfiguration => EvaluateSampleSend(inputs),
            _ => new(AutomationNodeRunState.Succeeded, "action-simulated", "complete"),
        };

    private static SampleNodeEvaluation EvaluateSampleCondition(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs
    ) =>
        inputs.GetValueOrDefault(new("predicate"))?.Value switch
        {
            AutomationValue.Boolean { Value: var result } => new(
                AutomationNodeRunState.Succeeded,
                result ? "condition-true" : "condition-false",
                result ? "yes" : "no"
            ),
            _ => new(AutomationNodeRunState.Failed, "condition-invalid", null),
        };

    private static SampleNodeEvaluation EvaluateSampleSend(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs
    ) =>
        inputs.TryGetValue(new("message"), out var message)
        && AutomationPublicSinkAdmission.AdmitText(message)
            is AutomationPublicTextAdmission.Admitted
            ? new(AutomationNodeRunState.Succeeded, "action-simulated", "complete")
            : new(AutomationNodeRunState.Failed, "sensitive-output-blocked", null);

    private static AutomationContext SampleContext(BotHost host, string sourceDefinitionId)
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        return new(
            new(Guid.NewGuid(), new(sourceDefinitionId)),
            new("sample-viewer", "sample_viewer", "Sample Viewer"),
            new(
                new(host.Id),
                host.TwitchUserId ?? string.Empty,
                host.Login,
                string.IsNullOrWhiteSpace(host.DisplayName) ? host.Login : host.DisplayName
            ),
            new("sample-stream", "Sample stream", "Just Chatting", now.AddHours(-1)),
            new(now, now),
            [new(0, "sample")],
            new(
                new Dictionary<AutomationVariableName, AutomationVariable>
                {
                    [new("viewer_count")] = new(
                        new AutomationValue.Number(24),
                        AutomationDataSensitivity.Safe
                    ),
                    [new("bits")] = new(
                        new AutomationValue.Number(100),
                        AutomationDataSensitivity.Safe
                    ),
                    [new("category")] = new(
                        new AutomationValue.Text("Just Chatting"),
                        AutomationDataSensitivity.Safe
                    ),
                }
            )
        );
    }

    private static ImmutableArray<AutomationNodeId> Outgoing(
        IEnumerable<AutomationFlowDraftEdge> edges,
        AutomationNodeId sourceNodeId,
        string? sourcePort
    ) =>
        edges
            .Where(edge =>
                edge.Kind == AutomationEdgeKind.Flow
                && edge.SourceNodeId == sourceNodeId
                && (sourcePort is null || edge.SourcePortId.Value == sourcePort)
            )
            .OrderBy(static edge => edge.SourcePortId.Value, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetNodeId.Value)
            .Select(static edge => edge.TargetNodeId)
            .ToImmutableArray();

    private sealed record SampleNodeEvaluation(
        AutomationNodeRunState State,
        string OutcomeCode,
        string? SourcePort
    );
}
