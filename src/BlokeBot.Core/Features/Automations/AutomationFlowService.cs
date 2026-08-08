using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationFlowService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationCatalogService catalog,
    AutomationExpressionService expressions,
    IOverlayCueAdmissionService overlayCues,
    TimeProvider clock,
    IEventSubChannelReconciliationTrigger? eventSub = null
)
{
    public async Task<AutomationFlowSaveOutcome> SaveAsync(
        AutomationFlowDraft draft,
        CancellationToken cancellationToken
    )
    {
        var validation = await ValidateAsync(draft, cancellationToken);
        if (validation.Gate is { } gate)
        {
            return gate switch
            {
                AutomationCatalogAvailability.Disabled =>
                    new AutomationFlowSaveOutcome.FeatureDisabled(),
                AutomationCatalogAvailability.HostNotFound =>
                    new AutomationFlowSaveOutcome.HostNotFound(),
                _ => throw new InvalidOperationException("Unexpected automation catalog state."),
            };
        }

        if (!validation.Errors.IsEmpty)
        {
            return new AutomationFlowSaveOutcome.Invalid(validation.Errors);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AutomationFlow flow;
        if (draft.Id is { } existingId)
        {
            flow =
                await db.AutomationFlows.SingleOrDefaultAsync(
                    value => value.Id == existingId.Value && value.HostId == draft.HostId.Value,
                    cancellationToken
                ) ?? null!;
            if (flow is null)
            {
                return new AutomationFlowSaveOutcome.FlowNotFound();
            }

            _ = await db
                .AutomationFlowEdges.Where(value => value.FlowId == flow.Id)
                .ExecuteDeleteAsync(cancellationToken);
            _ = await db
                .AutomationFlowNodes.Where(value => value.FlowId == flow.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            flow = new AutomationFlow
            {
                Id = Guid.NewGuid(),
                HostId = draft.HostId.Value,
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            };
            _ = db.AutomationFlows.Add(flow);
        }

        flow.Name = draft.Name.Trim();
        flow.SchemaVersion = draft.SchemaVersion;
        flow.IsEnabled = draft.IsEnabled;
        flow.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        db.AutomationFlowNodes.AddRange(draft.Nodes.Select(node => Persist(flow.Id, node)));
        db.AutomationFlowEdges.AddRange(draft.Edges.Select(edge => Persist(flow.Id, edge)));
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await ReconcileEventSubAsync(cancellationToken);
        return new AutomationFlowSaveOutcome.Saved(new(flow.Id));
    }

    public async Task<AutomationFlowEnableOutcome> SetEnabledAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        var availability = await catalog.DiscoverAsync(hostId, cancellationToken);
        if (availability.Availability == AutomationCatalogAvailability.Disabled)
        {
            return new AutomationFlowEnableOutcome.FeatureDisabled();
        }

        if (availability.Availability == AutomationCatalogAvailability.HostNotFound)
        {
            return new AutomationFlowEnableOutcome.HostNotFound();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var flow = await db
            .AutomationFlows.Include(static value => value.Nodes)
            .Include(static value => value.Edges)
            .SingleOrDefaultAsync(
                value => value.Id == flowId.Value && value.HostId == hostId.Value,
                cancellationToken
            );
        if (flow is null)
        {
            return new AutomationFlowEnableOutcome.FlowNotFound();
        }

        var enabledFeatures = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId.Value)
            .Select(static value => value.EnabledFeatures)
            .SingleAsync(cancellationToken);
        var required = AutomationRequiredFeatures.ForDefinitions(
            flow.Nodes.Select(static node => node.DefinitionId)
        );
        if (!enabledFeatures.Contains(required))
        {
            return new AutomationFlowEnableOutcome.FeatureDisabled();
        }

        if (enabled)
        {
            var validation = await ValidateAsync(Draft(flow, enabled), cancellationToken);
            if (!validation.Errors.IsEmpty)
            {
                return new AutomationFlowEnableOutcome.Invalid(validation.Errors);
            }
        }

        flow.IsEnabled = enabled;
        flow.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        _ = await db.SaveChangesAsync(cancellationToken);
        await ReconcileEventSubAsync(cancellationToken);
        return new AutomationFlowEnableOutcome.Updated();
    }

    private async Task ReconcileEventSubAsync(CancellationToken cancellationToken)
    {
        // Enabled-flow changes alter which EventSub subscriptions the host runtime needs.
        if (eventSub is not null)
        {
            await eventSub.ReconcileAsync(cancellationToken);
        }
    }

    internal async Task<AutomationGraphValidation> ValidateAsync(
        AutomationFlowDraft draft,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await catalog.DiscoverAsync(draft.HostId, cancellationToken);
        if (snapshot.Availability != AutomationCatalogAvailability.Enabled)
        {
            return new(snapshot.Availability, []);
        }

        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            errors.Add(new(null, "name-required", "Enter a flow name."));
        }
        else if (draft.Name.Trim().Length > 200)
        {
            errors.Add(new(null, "name-too-long", "Flow names cannot exceed 200 characters."));
        }

        if (draft.SchemaVersion != AutomationFlowSchema.CurrentVersion)
        {
            errors.Add(new(null, "schema-invalid", "Choose a supported flow schema version."));
        }

        var definitions = snapshot.Definitions.ToDictionary(static value => value.Id);
        var nodes = new Dictionary<AutomationNodeId, AutomationFlowDraftNode>();
        foreach (var node in draft.Nodes)
        {
            if (node.Id.Value == Guid.Empty || !nodes.TryAdd(node.Id, node))
            {
                errors.Add(new(node.Id, "node-id-invalid", "Every node needs a unique identity."));
                continue;
            }

            await ValidateNodeAsync(draft.HostId, node, definitions, errors, cancellationToken);
        }

        var sources = nodes
            .Values.Where(node =>
                definitions.TryGetValue(new(node.Definition.TypeId), out var definition)
                && definition.Kind == AutomationNodeKind.Source
            )
            .ToArray();
        if (sources.Length != 1)
        {
            errors.Add(
                new(null, "source-count", "A flow must contain exactly one event source node.")
            );
        }

        var incoming = nodes.Keys.ToDictionary(static id => id, static _ => 0);
        var adjacency = nodes.Keys.ToDictionary(
            static id => id,
            static _ => new List<AutomationNodeId>()
        );
        var edgeIds = new HashSet<Guid>();
        foreach (var edge in draft.Edges)
        {
            if (edge.Id == Guid.Empty || !edgeIds.Add(edge.Id))
            {
                errors.Add(new(null, "edge-id-invalid", "Every edge needs a unique identity."));
            }

            if (
                !nodes.TryGetValue(edge.SourceNodeId, out var source)
                || !nodes.TryGetValue(edge.TargetNodeId, out var target)
            )
            {
                errors.Add(new(null, "edge-node-missing", "Every edge must connect saved nodes."));
                continue;
            }

            incoming[edge.TargetNodeId]++;
            adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
            ValidateEdge(edge, source, target, definitions, errors);
        }

        foreach (var (nodeId, count) in incoming)
        {
            var isSource =
                nodes.TryGetValue(nodeId, out var node)
                && definitions.TryGetValue(new(node.Definition.TypeId), out var definition)
                && definition.Kind == AutomationNodeKind.Source;
            if (isSource && count != 0)
            {
                errors.Add(
                    new(nodeId, "source-incoming", "An event source cannot have an incoming edge.")
                );
            }
            else if (!isSource && count != 1)
            {
                errors.Add(
                    new(
                        nodeId,
                        count > 1 ? "join-not-supported" : "node-disconnected",
                        count > 1
                            ? "A v1 flow node cannot join multiple incoming branches."
                            : "Every non-source node must have one incoming edge."
                    )
                );
            }
        }

        if (sources.Length == 1)
        {
            var reached = Reachable(sources[0].Id, adjacency);
            foreach (var nodeId in nodes.Keys.Where(nodeId => !reached.Contains(nodeId)))
            {
                errors.Add(
                    new(nodeId, "node-disconnected", "Every node must connect to the event source.")
                );
            }
        }

        if (HasCycle(adjacency))
        {
            errors.Add(new(null, "cycle", "Automation flows cannot contain cycles."));
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var enabledFeatures = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == draft.HostId.Value)
            .Select(static value => value.EnabledFeatures)
            .SingleAsync(cancellationToken);
        var required = AutomationRequiredFeatures.ForDefinitions(
            draft.Nodes.Select(static node => node.Definition.TypeId)
        );
        return enabledFeatures.Contains(required)
            ? new(null, errors.ToImmutable())
            : new(AutomationCatalogAvailability.Disabled, []);
    }

    private async Task ValidateNodeAsync(
        AutomationHostId hostId,
        AutomationFlowDraftNode node,
        IReadOnlyDictionary<AutomationDefinitionId, AutomationDefinitionDescriptor> definitions,
        ImmutableArray<AutomationGraphError>.Builder errors,
        CancellationToken cancellationToken
    )
    {
        var definitionId = new AutomationDefinitionId(node.Definition.TypeId);
        var check = await catalog.ValidatePersistedForSaveAsync(
            hostId,
            node.Definition,
            cancellationToken
        );
        if (check is not AutomationConfigurationCheck.Valid valid)
        {
            errors.Add(
                new(
                    node.Id,
                    "configuration-invalid",
                    "The node type, schema, or configuration is not valid."
                )
            );
            return;
        }

        if (!Enum.IsDefined(node.FailurePolicy))
        {
            errors.Add(
                new(node.Id, "failure-policy-invalid", "Choose stop or continue on failure.")
            );
        }

        if (node.ExpressionLanguageVersion != AutomationExpressionLanguage.CurrentVersion)
        {
            errors.Add(
                new(
                    node.Id,
                    "expression-version-unsupported",
                    "The node expression language version is not supported."
                )
            );
        }

        if (
            valid.Configuration is ConditionControlConfiguration condition
            && expressions.Validate(
                new(AutomationExpressionLanguage.CurrentVersion, condition.Expression)
            ) is AutomationExpressionCheck.Invalid
        )
        {
            errors.Add(new(node.Id, "condition-invalid", "The condition expression is not valid."));
        }

        if (
            valid.Configuration is SendChatActionConfiguration sendChat
            && expressions.ValidateTemplate(sendChat.Message) is AutomationExpressionCheck.Invalid
        )
        {
            errors.Add(
                new(
                    node.Id,
                    "action-expression-invalid",
                    "The chat message expression is not valid."
                )
            );
        }

        if (valid.Configuration is PlayOverlayCueActionConfiguration cue)
        {
            var references = await overlayCues.ResolveReferencesAsync(
                new(hostId.Value, cue.TargetId.Value, cue.CueId.Value),
                cancellationToken
            );
            if (references is not OverlayCueReferenceOutcome.Available)
            {
                errors.Add(
                    new(
                        node.Id,
                        "overlay-reference-unavailable",
                        "Choose an available Cue player and saved cue for this channel."
                    )
                );
            }
        }

        if (valid.Configuration is RewardRedemptionSourceConfiguration { RewardId: { } rewardId })
        {
            // The reward filter is a reference resolved against this channel's known rewards,
            // never free-text. Externally created rewards remain valid read-only triggers.
            await using var rewardDb = await dbFactory.CreateDbContextAsync(cancellationToken);
            var known = await rewardDb
                .TwitchCustomRewards.AsNoTracking()
                .AnyAsync(
                    reward => reward.HostId == hostId.Value && reward.ProviderRewardId == rewardId,
                    cancellationToken
                );
            if (!known)
            {
                errors.Add(
                    new(
                        node.Id,
                        "reward-reference-unavailable",
                        "Choose a Custom Reward that exists on this channel."
                    )
                );
            }
        }

        if (!definitions.TryGetValue(definitionId, out var descriptor))
        {
            return;
        }

        foreach (var (fieldId, expression) in node.FieldExpressions)
        {
            if (
                descriptor.Kind != AutomationNodeKind.Action
                || !descriptor.Configuration.Any(field => field.Id == fieldId)
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "action-field-invalid",
                        "The expression must target an action configuration field."
                    )
                );
            }
            else if (expressions.Validate(expression) is AutomationExpressionCheck.Invalid)
            {
                errors.Add(
                    new(node.Id, "action-expression-invalid", "The action expression is not valid.")
                );
            }
        }
    }

    private static void ValidateEdge(
        AutomationFlowDraftEdge edge,
        AutomationFlowDraftNode source,
        AutomationFlowDraftNode target,
        IReadOnlyDictionary<AutomationDefinitionId, AutomationDefinitionDescriptor> definitions,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        if (
            !definitions.TryGetValue(new(source.Definition.TypeId), out var sourceDefinition)
            || !definitions.TryGetValue(new(target.Definition.TypeId), out var targetDefinition)
        )
        {
            return;
        }

        var output = sourceDefinition.Outputs.SingleOrDefault(port => port.Id == edge.SourcePortId);
        var input = targetDefinition.Inputs.SingleOrDefault(port => port.Id == edge.TargetPortId);
        if (output is null || input is null)
        {
            errors.Add(
                new(edge.TargetNodeId, "port-missing", "The edge references an unavailable port.")
            );
        }
        else if (
            output.ValueType != input.ValueType
            || output.Sensitivity != input.Sensitivity
            || output.ValueType != AutomationPortValueType.Flow
        )
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "port-incompatible",
                    "The connected ports are not type-compatible flow ports."
                )
            );
        }
    }

    private static HashSet<AutomationNodeId> Reachable(
        AutomationNodeId source,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> adjacency
    )
    {
        var reached = new HashSet<AutomationNodeId>();
        var pending = new Stack<AutomationNodeId>();
        pending.Push(source);
        while (pending.TryPop(out var nodeId) && reached.Add(nodeId))
        {
            foreach (var target in adjacency[nodeId])
            {
                pending.Push(target);
            }
        }

        return reached;
    }

    private static bool HasCycle(
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> adjacency
    )
    {
        var remainingIncoming = adjacency.Keys.ToDictionary(static id => id, static _ => 0);
        foreach (var targets in adjacency.Values)
        {
            foreach (var target in targets)
            {
                remainingIncoming[target]++;
            }
        }

        var ready = new Queue<AutomationNodeId>(
            remainingIncoming.Where(static pair => pair.Value == 0).Select(static pair => pair.Key)
        );
        var visited = 0;
        while (ready.TryDequeue(out var nodeId))
        {
            visited++;
            foreach (var target in adjacency[nodeId])
            {
                remainingIncoming[target]--;
                if (remainingIncoming[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        return visited != adjacency.Count;
    }

    private static AutomationFlowNode Persist(Guid flowId, AutomationFlowDraftNode node) =>
        new()
        {
            Id = node.Id.Value,
            FlowId = flowId,
            DefinitionId = node.Definition.TypeId,
            DefinitionSchemaVersion = node.Definition.SchemaVersion,
            ConfigurationJson = node.Definition.Configuration.GetRawText(),
            FieldExpressionsJson = AutomationRuntimeSerialization.SerializeExpressions(
                node.FieldExpressions
            ),
            ExpressionLanguageVersion = node.ExpressionLanguageVersion.Value,
            ContinueOnFailure = node.FailurePolicy == AutomationNodeFailurePolicy.Continue,
        };

    private static AutomationFlowEdge Persist(Guid flowId, AutomationFlowDraftEdge edge) =>
        new()
        {
            Id = edge.Id,
            FlowId = flowId,
            SourceNodeId = edge.SourceNodeId.Value,
            SourcePortId = edge.SourcePortId.Value,
            TargetNodeId = edge.TargetNodeId.Value,
            TargetPortId = edge.TargetPortId.Value,
        };

    internal static AutomationFlowDraft Draft(AutomationFlow flow, bool? enabled = null) =>
        new(
            new(flow.Id),
            new(flow.HostId),
            flow.Name,
            flow.SchemaVersion,
            enabled ?? flow.IsEnabled,
            flow.Nodes.Select(static node => new AutomationFlowDraftNode(
                    new(node.Id),
                    new(
                        node.DefinitionId,
                        node.DefinitionSchemaVersion,
                        JsonDocument.Parse(node.ConfigurationJson).RootElement.Clone()
                    ),
                    new(node.ExpressionLanguageVersion),
                    node.ContinueOnFailure
                        ? AutomationNodeFailurePolicy.Continue
                        : AutomationNodeFailurePolicy.Stop,
                    AutomationRuntimeSerialization.DeserializeExpressions(node.FieldExpressionsJson)
                ))
                .ToImmutableArray(),
            flow.Edges.Select(static edge => new AutomationFlowDraftEdge(
                    edge.Id,
                    new(edge.SourceNodeId),
                    new(edge.SourcePortId),
                    new(edge.TargetNodeId),
                    new(edge.TargetPortId)
                ))
                .ToImmutableArray()
        );
}

internal sealed record AutomationGraphValidation(
    AutomationCatalogAvailability? Gate,
    ImmutableArray<AutomationGraphError> Errors
);
