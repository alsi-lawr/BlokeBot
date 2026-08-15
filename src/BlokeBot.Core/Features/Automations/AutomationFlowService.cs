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
        return new AutomationFlowQueryOutcome.Available(
            flows
                .Select(static flow => new AutomationFlowSnapshot(
                    Draft(flow),
                    new DateTimeOffset(flow.CreatedAtUtc, TimeSpan.Zero),
                    new DateTimeOffset(flow.UpdatedAtUtc, TimeSpan.Zero)
                ))
                .ToImmutableArray()
        );
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

        var original = Draft(flow);
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

    public async Task<AutomationSampleRunOutcome> RunSampleAsync(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
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
        return EvaluateSample(
            draft,
            sourceNodeId,
            SampleContext(
                host,
                sourceDefinitionId ?? AutomationDefinitionIds.IncomingRaidSource.Value
            )
        );
    }

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
        flow.UseVerticalLayout = draft.Canvas.Orientation == AutomationFlowOrientation.Vertical;
        flow.UseSmoothEdges = draft.Canvas.EdgeStyle == AutomationEdgeStyle.Smooth;
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
        var capabilityErrors = CapabilityUnavailableErrors(
            flow.Nodes.Select(static node => (new AutomationNodeId(node.Id), node.DefinitionId)),
            enabledFeatures
        );
        if (enabled && !capabilityErrors.IsEmpty)
        {
            return new AutomationFlowEnableOutcome.Invalid(capabilityErrors);
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
            errors.Add(new(null, "name-too-long", "Use 200 characters or fewer in the flow name."));
        }

        if (draft.SchemaVersion != AutomationFlowSchema.CurrentVersion)
        {
            errors.Add(new(null, "schema-invalid", "Choose a supported flow schema version."));
        }

        if (!Enum.IsDefined(draft.Canvas.Orientation) || !Enum.IsDefined(draft.Canvas.EdgeStyle))
        {
            errors.Add(new(null, "canvas-settings-invalid", "Select a supported flow layout."));
        }

        var definitions = snapshot.Definitions.ToDictionary(static value => value.Id);
        foreach (
            var definitionId in draft.Nodes.Select(static node => new AutomationDefinitionId(
                node.Definition.TypeId
            ))
        )
        {
            if (
                !definitions.ContainsKey(definitionId)
                && catalog.TryDescribe(definitionId, out var descriptor)
            )
            {
                definitions.Add(definitionId, descriptor);
            }
        }
        var nodes = new Dictionary<AutomationNodeId, AutomationFlowDraftNode>();
        foreach (var node in draft.Nodes)
        {
            if (node.Id.Value == Guid.Empty || !nodes.TryAdd(node.Id, node))
            {
                errors.Add(new(node.Id, "node-id-invalid", "Delete this duplicate node."));
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
        if (sources.Length == 0)
        {
            errors.Add(new(null, "source-count", "Add one or more trigger nodes."));
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
                errors.Add(new(null, "edge-id-invalid", "Delete the duplicate connection."));
            }

            if (
                !nodes.TryGetValue(edge.SourceNodeId, out var source)
                || !nodes.TryGetValue(edge.TargetNodeId, out var target)
            )
            {
                errors.Add(new(null, "edge-node-missing", "Reconnect the saved nodes."));
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
                    new(nodeId, "source-incoming", "Remove the input connection from this trigger.")
                );
            }
            else if (!isSource && count == 0)
            {
                errors.Add(
                    new(
                        nodeId,
                        "node-disconnected",
                        "Connect this node to a trigger or another node."
                    )
                );
            }
        }

        if (sources.Length > 0)
        {
            var reached = Reachable(sources.Select(static source => source.Id), adjacency);
            foreach (var nodeId in nodes.Keys.Where(nodeId => !reached.Contains(nodeId)))
            {
                errors.Add(new(nodeId, "node-disconnected", "Connect this node to a trigger."));
            }
        }

        ValidateTriggerContexts(nodes, definitions, sources, adjacency, errors);

        if (HasCycle(adjacency))
        {
            errors.Add(new(null, "cycle", "Remove the connection that creates a loop."));
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var enabledFeatures = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == draft.HostId.Value)
            .Select(static value => value.EnabledFeatures)
            .SingleAsync(cancellationToken);
        errors.AddRange(
            CapabilityUnavailableErrors(
                draft.Nodes.Select(static node => (node.Id, node.Definition.TypeId)),
                enabledFeatures
            )
        );

        return new(null, errors.ToImmutable());
    }

    private static void ValidateTriggerContexts(
        IReadOnlyDictionary<AutomationNodeId, AutomationFlowDraftNode> nodes,
        IReadOnlyDictionary<AutomationDefinitionId, AutomationDefinitionDescriptor> definitions,
        IReadOnlyCollection<AutomationFlowDraftNode> sources,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> adjacency,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        foreach (var node in nodes.Values)
        {
            if (
                !definitions.TryGetValue(new(node.Definition.TypeId), out var definition)
                || definition.TriggerContextRequirement is not { } requirement
            )
            {
                continue;
            }

            var hasCompatiblePath = sources
                .Where(source =>
                    requirement.CompatibleSources.Contains(new(source.Definition.TypeId))
                )
                .Any(source => Reachable([source.Id], adjacency).Contains(node.Id));
            if (!hasCompatiblePath)
            {
                errors.Add(
                    new(node.Id, "trigger-context-incompatible", requirement.ValidationMessage)
                );
            }
        }
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
        if (check is AutomationConfigurationCheck.Invalid invalid)
        {
            foreach (var error in invalid.Errors)
            {
                errors.Add(
                    new(
                        node.Id,
                        "configuration-invalid",
                        error.Message,
                        error.Target is AutomationValidationTarget.Field field ? field.Id : null
                    )
                );
            }

            return;
        }

        if (check is not AutomationConfigurationCheck.Valid valid)
        {
            errors.Add(
                new(node.Id, "configuration-invalid", "Restore this node type, or delete the node.")
            );
            return;
        }

        if (!Enum.IsDefined(node.FailurePolicy))
        {
            errors.Add(new(node.Id, "failure-policy-invalid", "Choose Stop or Continue."));
        }

        if (node.ExpressionLanguageVersion != AutomationExpressionLanguage.CurrentVersion)
        {
            errors.Add(
                new(
                    node.Id,
                    "expression-version-unsupported",
                    "Replace this node. Its expression version is not supported."
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
            errors.Add(
                new(
                    node.Id,
                    "condition-invalid",
                    "Enter a valid condition expression.",
                    new("expression")
                )
            );
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
                    "Enter a valid chat message expression.",
                    new("message")
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
                        "Choose an available Cue player and saved cue."
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
                        "Choose a Custom Reward from this channel."
                    )
                );
            }
        }

        if (valid.Configuration is CustomCommandSourceConfiguration command)
        {
            await using var commandDb = await dbFactory.CreateDbContextAsync(cancellationToken);
            var known = await commandDb
                .CustomCommands.AsNoTracking()
                .AnyAsync(
                    candidate =>
                        candidate.HostId == hostId.Value && candidate.Id == command.CommandId.Value,
                    cancellationToken
                );
            if (!known)
            {
                errors.Add(
                    new(
                        node.Id,
                        "custom-command-reference-unavailable",
                        "Choose a custom command from this channel."
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
                    new(node.Id, "action-field-invalid", "Select a field from this action.")
                );
            }
            else if (expressions.Validate(expression) is AutomationExpressionCheck.Invalid)
            {
                errors.Add(
                    new(node.Id, "action-expression-invalid", "Enter a valid action expression.")
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
                new(edge.TargetNodeId, "port-missing", "Reconnect this node to an available port.")
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
                    "Connect ports that have the same type."
                )
            );
        }
    }

    private static HashSet<AutomationNodeId> Reachable(
        IEnumerable<AutomationNodeId> sources,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> adjacency
    )
    {
        var reached = new HashSet<AutomationNodeId>();
        var pending = new Stack<AutomationNodeId>();
        foreach (var source in sources)
        {
            pending.Push(source);
        }
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
            CanvasX = node.Position.X.Value,
            CanvasY = node.Position.Y.Value,
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
                    AutomationRuntimeSerialization.DeserializeExpressions(
                        node.FieldExpressionsJson
                    ),
                    new(new(node.CanvasX), new(node.CanvasY))
                ))
                .ToImmutableArray(),
            flow.Edges.Select(static edge => new AutomationFlowDraftEdge(
                    edge.Id,
                    new(edge.SourceNodeId),
                    new(edge.SourcePortId),
                    new(edge.TargetNodeId),
                    new(edge.TargetPortId)
                ))
                .ToImmutableArray(),
            new(
                flow.UseVerticalLayout
                    ? AutomationFlowOrientation.Vertical
                    : AutomationFlowOrientation.Horizontal,
                flow.UseSmoothEdges ? AutomationEdgeStyle.Smooth : AutomationEdgeStyle.Angular
            )
        );

    private static string DuplicateName(string name)
    {
        const string Prefix = "Copy of ";
        var maximumOriginalLength = 200 - Prefix.Length;
        return Prefix + name[..Math.Min(name.Length, maximumOriginalLength)];
    }

    private static ImmutableArray<AutomationGraphError> CapabilityUnavailableErrors(
        IEnumerable<(AutomationNodeId NodeId, string DefinitionId)> nodes,
        HostFeatureFlags enabled
    ) =>
        [
            .. nodes
                .Select(node =>
                    (
                        node.NodeId,
                        Unavailable: AutomationRequiredFeatures.ForDefinitions([node.DefinitionId])
                            & ~enabled
                    )
                )
                .Where(static node => node.Unavailable != HostFeatureFlags.None)
                .Select(static node => CapabilityUnavailableError(node.NodeId, node.Unavailable)),
        ];

    private static AutomationGraphError CapabilityUnavailableError(
        AutomationNodeId nodeId,
        HostFeatureFlags unavailable
    )
    {
        var names = HostFeatureCatalog
            .Cards(unavailable)
            .Where(static card => card.Enabled)
            .Select(static card => card.Name)
            .ToArray();
        return new(
            nodeId,
            "capability-unavailable",
            names.Length == 0
                ? "Turn on the required channel tool in Channel setup."
                : $"Turn on {string.Join(", ", names)} in Channel setup."
        );
    }

    private AutomationSampleRunOutcome EvaluateSample(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        AutomationContext context
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

            var evaluated = EvaluateSampleNode(valid.Configuration, context);
            outcomes.Add(new(node.Id, evaluated.State, evaluated.OutcomeCode));
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

    private SampleNodeEvaluation EvaluateSampleNode(
        AutomationConfiguration configuration,
        AutomationContext context
    ) =>
        configuration switch
        {
            ConditionControlConfiguration condition => EvaluateSampleCondition(condition, context),
            DelayControlConfiguration => new(
                AutomationNodeRunState.Succeeded,
                "delay-skipped",
                "complete"
            ),
            SendChatActionConfiguration chat
                when expressions.Interpolate(chat.Message, context)
                    is AutomationExpressionResult.Invalid => new(
                AutomationNodeRunState.Failed,
                "action-expression-invalid",
                null
            ),
            _ => new(AutomationNodeRunState.Succeeded, "action-simulated", "complete"),
        };

    private SampleNodeEvaluation EvaluateSampleCondition(
        ConditionControlConfiguration condition,
        AutomationContext context
    ) =>
        expressions.Evaluate(
            new(AutomationExpressionLanguage.CurrentVersion, condition.Expression),
            context
        ) switch
        {
            AutomationExpressionResult.Value { Result: bool result } => new(
                AutomationNodeRunState.Succeeded,
                result ? "condition-true" : "condition-false",
                result ? "true" : "false"
            ),
            _ => new(AutomationNodeRunState.Failed, "condition-invalid", null),
        };

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
                edge.SourceNodeId == sourceNodeId
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

internal sealed record AutomationGraphValidation(
    AutomationCatalogAvailability? Gate,
    ImmutableArray<AutomationGraphError> Errors
);
