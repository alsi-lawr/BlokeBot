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
                    new DateTimeOffset(flow.UpdatedAtUtc, TimeSpan.Zero)
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
        return await EvaluateSampleAsync(
            draft,
            sourceNodeId,
            SampleContext(
                host,
                sourceDefinitionId ?? AutomationDefinitionIds.IncomingRaidSource.Value
            ),
            cancellationToken
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
            if (
                RestoreDraft(flow, enabled)
                is not AutomationFlowDraftRestoreOutcome.Available restored
            )
            {
                return new AutomationFlowEnableOutcome.Invalid([MalformedGraphError()]);
            }

            var validation = await ValidateAsync(restored.Draft, cancellationToken);
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

    internal Task<AutomationGraphValidation> ValidateAsync(
        AutomationFlowDraft draft,
        CancellationToken cancellationToken
    ) => ValidateAsync(draft, AutomationGraphAdmission.Saved, cancellationToken);

    internal Task<AutomationGraphValidation> ValidateFrozenDefinitionAsync(
        AutomationHostId hostId,
        AutomationRuntimeSerialization.PersistedFlow flow,
        CancellationToken cancellationToken
    ) =>
        flow.HostId == hostId.Value
        && RestoreFrozenDraft(flow) is AutomationFlowDraftRestoreOutcome.Available restored
            ? ValidateAsync(restored.Draft, AutomationGraphAdmission.Frozen, cancellationToken)
            : Task.FromResult<AutomationGraphValidation>(new(null, [MalformedGraphError()]));

    private async Task<AutomationGraphValidation> ValidateAsync(
        AutomationFlowDraft draft,
        AutomationGraphAdmission admission,
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

        var definitions = new Dictionary<AutomationNodeId, AutomationDefinitionDescriptor>();
        var nodes = new Dictionary<AutomationNodeId, AutomationFlowDraftNode>();
        foreach (var node in draft.Nodes)
        {
            if (node.Id.Value == Guid.Empty || !nodes.TryAdd(node.Id, node))
            {
                errors.Add(new(node.Id, "node-id-invalid", "Delete this duplicate node."));
                continue;
            }

            if (node.DisplayAlias?.Length > AutomationFlowSchema.NodeDisplayAliasMaximumLength)
            {
                errors.Add(
                    new(
                        node.Id,
                        "node-alias-too-long",
                        $"Use {AutomationFlowSchema.NodeDisplayAliasMaximumLength} characters or fewer in the node name."
                    )
                );
            }

            await ValidateNodeAsync(draft.HostId, node, errors, admission, cancellationToken);
            if (
                catalog.ValidatePersistedDefinition(node.Definition)
                is AutomationConfigurationCheck.Valid valid
            )
            {
                definitions[node.Id] = valid.Definition;
            }
        }

        var sources = nodes
            .Values.Where(node =>
                definitions.TryGetValue(node.Id, out var definition)
                && definition.Kind == AutomationNodeKind.Source
            )
            .ToArray();
        if (sources.Length == 0)
        {
            errors.Add(new(null, "source-count", "Add one or more trigger nodes."));
        }

        var flowIncoming = nodes.Keys.ToDictionary(static id => id, static _ => 0);
        var flowAdjacency = nodes.Keys.ToDictionary(
            static id => id,
            static _ => new List<AutomationNodeId>()
        );
        var dataAdjacency = nodes.Keys.ToDictionary(
            static id => id,
            static _ => new List<AutomationNodeId>()
        );
        var combinedAdjacency = nodes.Keys.ToDictionary(
            static id => id,
            static _ => new List<AutomationNodeId>()
        );
        var dataIncoming = new Dictionary<(AutomationNodeId, AutomationPortId), int>();
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

            if (!Enum.IsDefined(edge.Kind))
            {
                errors.Add(
                    new(edge.TargetNodeId, "edge-kind-invalid", "Reconnect this saved connection.")
                );
                continue;
            }

            if (edge.Kind == AutomationEdgeKind.Flow)
            {
                flowIncoming[edge.TargetNodeId]++;
                flowAdjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
            }
            else
            {
                var input = (edge.TargetNodeId, edge.TargetPortId);
                dataIncoming[input] = dataIncoming.GetValueOrDefault(input) + 1;
                dataAdjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
            }

            combinedAdjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
            ValidateEdge(edge, source, target, definitions, errors);
        }

        foreach (var ((nodeId, _), count) in dataIncoming.Where(static pair => pair.Value > 1))
        {
            errors.Add(
                new(nodeId, "data-input-duplicate", "Keep only one Data connection for this input.")
            );
        }

        ValidateBindings(nodes, definitions, dataIncoming, errors);

        foreach (var (nodeId, count) in flowIncoming)
        {
            var kind =
                nodes.TryGetValue(nodeId, out var node)
                && definitions.TryGetValue(node.Id, out var definition)
                    ? definition.Kind
                    : (AutomationNodeKind?)null;
            if (kind == AutomationNodeKind.Source && count != 0)
            {
                errors.Add(
                    new(nodeId, "source-incoming", "Remove the input connection from this trigger.")
                );
            }
            else if (kind is AutomationNodeKind.Action or AutomationNodeKind.Control && count == 0)
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
            var reached = Reachable(sources.Select(static source => source.Id), flowAdjacency);
            foreach (
                var nodeId in nodes.Keys.Where(nodeId =>
                    definitions.TryGetValue(nodeId, out var definition)
                    && definition.Kind is AutomationNodeKind.Action or AutomationNodeKind.Control
                    && !reached.Contains(nodeId)
                )
            )
            {
                errors.Add(new(nodeId, "node-disconnected", "Connect this node to a trigger."));
            }
        }

        ValidateTriggerContexts(nodes, definitions, sources, flowAdjacency, errors);
        ValidateSourceAvailability(nodes, definitions, sources, draft.Edges, flowAdjacency, errors);
        ValidateSafeTriggerExpressions(draft, definitions, errors);

        if (HasCycle(flowAdjacency))
        {
            errors.Add(new(null, "flow-cycle", "Remove the Flow connection that creates a loop."));
        }

        if (HasCycle(dataAdjacency))
        {
            errors.Add(new(null, "data-cycle", "Remove the Data connection that creates a loop."));
        }

        if (!HasCycle(flowAdjacency) && !HasCycle(dataAdjacency) && HasCycle(combinedAdjacency))
        {
            errors.Add(
                new(
                    null,
                    "dependency-cycle",
                    "Remove the Flow or Data connection that creates a dependency loop."
                )
            );
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
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        IReadOnlyCollection<AutomationFlowDraftNode> sources,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> adjacency,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        foreach (var node in nodes.Values)
        {
            if (
                !definitions.TryGetValue(node.Id, out var definition)
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
        ImmutableArray<AutomationGraphError>.Builder errors,
        AutomationGraphAdmission admission,
        CancellationToken cancellationToken
    )
    {
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
                        error.Target is AutomationValidationTarget.Field field ? field.Id : null,
                        error.Target is AutomationValidationTarget.Port port ? port.Id : null
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

        if (
            admission == AutomationGraphAdmission.Saved
            && valid.Configuration is PlayOverlayCueActionConfiguration cue
        )
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

        if (
            admission == AutomationGraphAdmission.Saved
            && valid.Configuration is RewardRedemptionSourceConfiguration { RewardId: { } rewardId }
        )
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

        if (
            admission == AutomationGraphAdmission.Saved
            && valid.Configuration is CustomCommandSourceConfiguration command
        )
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

        var descriptor = valid.Definition;

        foreach (var (fieldId, binding) in node.InputBindings)
        {
            if (!descriptor.Configuration.Any(field => field.Id == fieldId))
            {
                errors.Add(
                    new(
                        node.Id,
                        "binding-field-invalid",
                        "Select an input from this node.",
                        fieldId
                    )
                );
            }
            else if (
                !Enum.IsDefined(binding.Mode)
                || (
                    binding.Mode == AutomationInputBindingMode.Expression
                    && binding.Expression is null
                )
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "binding-mode-invalid",
                        "Choose Fixed, Connected, or Expression for this input.",
                        fieldId
                    )
                );
            }
            else if (
                binding.Expression is { } expression
                && descriptor.Kind != AutomationNodeKind.Transform
                && (
                    expression.LanguageVersion != AutomationExpressionLanguage.CurrentVersion
                    || expressions.Validate(expression) is AutomationExpressionCheck.Invalid
                )
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "binding-expression-invalid",
                        "Enter a valid input expression.",
                        fieldId
                    )
                );
            }
        }
    }

    private void ValidateSafeTriggerExpressions(
        AutomationFlowDraft draft,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        var flow = SampleFlow(draft);
        var service = new AutomationSafeTriggerExpressionService();
        foreach (var node in draft.Nodes)
        {
            if (
                !definitions.TryGetValue(node.Id, out var definition)
                || definition.Kind != AutomationNodeKind.Transform
                || flow.Nodes.FirstOrDefault(candidate => candidate.Id == node.Id.Value)
                    is not { } persisted
                || !AutomationSafeTriggerViewResolver.TryBuild(
                    catalog,
                    flow,
                    persisted,
                    out var safeView
                )
            )
            {
                continue;
            }

            foreach (var input in definition.Inputs)
            {
                if (
                    input.BindingFieldId is not { } fieldId
                    || !node.InputBindings.TryGetValue(fieldId, out var binding)
                    || binding.Mode != AutomationInputBindingMode.Expression
                    || binding.Expression is null
                )
                {
                    continue;
                }

                if (
                    !service.Validate(
                        binding.Expression,
                        input,
                        safeView,
                        out _,
                        out var invalidSafeField
                    )
                )
                {
                    errors.Add(
                        new(
                            node.Id,
                            "binding-expression-unavailable",
                            "Use only Safe trigger fields available on every Flow path.",
                            fieldId,
                            input.Id,
                            invalidSafeField
                        )
                    );
                }
            }
        }
    }

    private static void ValidateBindings(
        IReadOnlyDictionary<AutomationNodeId, AutomationFlowDraftNode> nodes,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        IReadOnlyDictionary<(AutomationNodeId, AutomationPortId), int> dataIncoming,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        foreach (var node in nodes.Values)
        {
            if (!definitions.TryGetValue(node.Id, out var definition))
            {
                continue;
            }

            foreach (
                var input in definition.Inputs.Where(static port =>
                    port.ValueType != AutomationPortValueType.Flow
                )
            )
            {
                if (
                    input.BindingFieldId is not { } fieldId
                    || !node.InputBindings.TryGetValue(fieldId, out var binding)
                )
                {
                    errors.Add(
                        new(
                            node.Id,
                            "binding-missing",
                            "Choose Fixed, Connected, or Expression for this input.",
                            input.BindingFieldId
                        )
                    );
                    continue;
                }

                var incoming = dataIncoming.GetValueOrDefault((node.Id, input.Id));
                if (binding.Mode == AutomationInputBindingMode.Connected && incoming != 1)
                {
                    errors.Add(
                        new(
                            node.Id,
                            "binding-connection-missing",
                            "Connect one Data output to this input.",
                            fieldId
                        )
                    );
                }
                else if (binding.Mode != AutomationInputBindingMode.Connected && incoming != 0)
                {
                    errors.Add(
                        new(
                            node.Id,
                            "binding-connection-inactive",
                            "Remove the Data connection or switch this input to Connected.",
                            fieldId
                        )
                    );
                }
            }
        }
    }

    private static void ValidateEdge(
        AutomationFlowDraftEdge edge,
        AutomationFlowDraftNode source,
        AutomationFlowDraftNode target,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        if (
            !definitions.TryGetValue(source.Id, out var sourceDefinition)
            || !definitions.TryGetValue(target.Id, out var targetDefinition)
        )
        {
            return;
        }

        var output = sourceDefinition.Outputs.SingleOrDefault(port => port.Id == edge.SourcePortId);
        var input = targetDefinition.Inputs.SingleOrDefault(port => port.Id == edge.TargetPortId);
        if (output is null || input is null)
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "port-missing",
                    "Reconnect this node to an available port.",
                    PortId: edge.TargetPortId
                )
            );
        }
        else if (edge.Kind == AutomationEdgeKind.Flow)
        {
            if (
                output.ValueType != AutomationPortValueType.Flow
                || input.ValueType != AutomationPortValueType.Flow
            )
            {
                errors.Add(
                    new(
                        edge.TargetNodeId,
                        "flow-port-incompatible",
                        "Connect Flow outputs only to Flow inputs.",
                        PortId: edge.TargetPortId
                    )
                );
            }
        }
        else if (
            output.ValueType == AutomationPortValueType.Flow
            || input.ValueType == AutomationPortValueType.Flow
            || output.ValueType != input.ValueType
        )
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "data-type-incompatible",
                    "Connect Data ports that have the same exact type.",
                    PortId: edge.TargetPortId
                )
            );
        }
        else if (sourceDefinition.Kind == AutomationNodeKind.Action)
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "data-source-incompatible",
                    "Use a trigger, Value, Transform, or Control output as Data.",
                    PortId: edge.TargetPortId
                )
            );
        }
        else if (
            output.Nullability == AutomationPortNullability.Nullable
            && input.Nullability == AutomationPortNullability.NonNullable
        )
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "data-nullability-incompatible",
                    "Connect this nullable output to an input that accepts null.",
                    PortId: edge.TargetPortId
                )
            );
        }
        else if (
            output.Sensitivity == AutomationDataSensitivity.Sensitive
            && input.Sensitivity == AutomationDataSensitivity.Safe
        )
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "data-sensitivity-incompatible",
                    "This input cannot accept Sensitive Data.",
                    PortId: edge.TargetPortId
                )
            );
        }
    }

    private static void ValidateSourceAvailability(
        IReadOnlyDictionary<AutomationNodeId, AutomationFlowDraftNode> nodes,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        IReadOnlyCollection<AutomationFlowDraftNode> sources,
        IEnumerable<AutomationFlowDraftEdge> edges,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> flowAdjacency,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        var dataInputs = edges
            .Where(static edge => edge.Kind == AutomationEdgeKind.Data)
            .GroupBy(static edge => edge.TargetNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static edge => edge.SourceNodeId).ToArray()
            );
        var sourceIds = sources.Select(static source => source.Id).ToHashSet();
        var cachedBackings = new Dictionary<AutomationNodeId, HashSet<AutomationNodeId>>();
        foreach (var edge in edges.Where(static edge => edge.Kind == AutomationEdgeKind.Data))
        {
            if (
                !nodes.TryGetValue(edge.TargetNodeId, out var target)
                || !definitions.TryGetValue(target.Id, out var targetDefinition)
                || targetDefinition.Kind
                    is not (AutomationNodeKind.Action or AutomationNodeKind.Control)
            )
            {
                continue;
            }

            var backings = SourceBackings(
                edge.SourceNodeId,
                sourceIds,
                dataInputs,
                cachedBackings,
                []
            );
            if (backings.Count == 0)
            {
                continue;
            }

            var reachingSources = sources
                .Where(source => Reachable([source.Id], flowAdjacency).Contains(edge.TargetNodeId))
                .Select(static source => source.Id)
                .ToHashSet();
            if (backings.Count != 1 || !reachingSources.SetEquals(backings))
            {
                errors.Add(
                    new(
                        edge.TargetNodeId,
                        "data-source-unavailable",
                        "This source Data is not available on every Flow path to the input."
                    )
                );
            }
        }
    }

    private static HashSet<AutomationNodeId> SourceBackings(
        AutomationNodeId nodeId,
        IReadOnlySet<AutomationNodeId> sources,
        IReadOnlyDictionary<AutomationNodeId, AutomationNodeId[]> dataInputs,
        IDictionary<AutomationNodeId, HashSet<AutomationNodeId>> cached,
        HashSet<AutomationNodeId> visiting
    )
    {
        if (cached.TryGetValue(nodeId, out var known))
        {
            return known;
        }

        if (!visiting.Add(nodeId))
        {
            return [];
        }

        var backings = sources.Contains(nodeId) ? new HashSet<AutomationNodeId> { nodeId } : [];
        if (dataInputs.TryGetValue(nodeId, out var producers))
        {
            foreach (var producer in producers)
            {
                backings.UnionWith(SourceBackings(producer, sources, dataInputs, cached, visiting));
            }
        }

        _ = visiting.Remove(nodeId);
        cached[nodeId] = backings;
        return backings;
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
            InputBindingsJson = AutomationRuntimeSerialization.SerializeInputBindings(
                node.InputBindings
            ),
            ExpressionLanguageVersion = node.ExpressionLanguageVersion.Value,
            ContinueOnFailure = node.FailurePolicy == AutomationNodeFailurePolicy.Continue,
            CanvasX = node.Position.X.Value,
            CanvasY = node.Position.Y.Value,
            DisplayAlias = string.IsNullOrWhiteSpace(node.DisplayAlias) ? null : node.DisplayAlias,
        };

    private static AutomationFlowEdge Persist(Guid flowId, AutomationFlowDraftEdge edge) =>
        new()
        {
            Id = edge.Id,
            FlowId = flowId,
            Kind = Persist(edge.Kind),
            SourceNodeId = edge.SourceNodeId.Value,
            SourcePortId = edge.SourcePortId.Value,
            TargetNodeId = edge.TargetNodeId.Value,
            TargetPortId = edge.TargetPortId.Value,
        };

    internal static AutomationFlowDraftRestoreOutcome RestoreDraft(
        AutomationFlow flow,
        bool? enabled = null
    )
    {
        var nodes = ImmutableArray.CreateBuilder<AutomationFlowDraftNode>();
        foreach (var node in flow.Nodes)
        {
            JsonElement configuration;
            try
            {
                configuration = JsonDocument.Parse(node.ConfigurationJson).RootElement.Clone();
            }
            catch (JsonException)
            {
                return new AutomationFlowDraftRestoreOutcome.Invalid();
            }

            if (
                AutomationRuntimeSerialization.RestoreInputBindings(node.InputBindingsJson)
                is not AutomationInputBindingsRestoreOutcome.Available bindings
            )
            {
                return new AutomationFlowDraftRestoreOutcome.Invalid();
            }

            nodes.Add(
                new(
                    new(node.Id),
                    new(node.DefinitionId, node.DefinitionSchemaVersion, configuration),
                    new(node.ExpressionLanguageVersion),
                    node.ContinueOnFailure
                        ? AutomationNodeFailurePolicy.Continue
                        : AutomationNodeFailurePolicy.Stop,
                    bindings.Bindings,
                    new(new(node.CanvasX), new(node.CanvasY)),
                    node.DisplayAlias
                )
            );
        }

        var edges = flow
            .Edges.Select(static edge => new AutomationFlowDraftEdge(
                edge.Id,
                Restore(edge.Kind),
                new(edge.SourceNodeId),
                new(edge.SourcePortId),
                new(edge.TargetNodeId),
                new(edge.TargetPortId)
            ))
            .ToImmutableArray();
        return edges.Any(static edge => !Enum.IsDefined(edge.Kind))
            ? new AutomationFlowDraftRestoreOutcome.Invalid()
            : new AutomationFlowDraftRestoreOutcome.Available(
                new(
                    new(flow.Id),
                    new(flow.HostId),
                    flow.Name,
                    flow.SchemaVersion,
                    enabled ?? flow.IsEnabled,
                    nodes.ToImmutable(),
                    edges,
                    new(
                        flow.UseVerticalLayout
                            ? AutomationFlowOrientation.Vertical
                            : AutomationFlowOrientation.Horizontal,
                        flow.UseSmoothEdges
                            ? AutomationEdgeStyle.Smooth
                            : AutomationEdgeStyle.Angular
                    )
                )
            );
    }

    private static AutomationFlowDraftRestoreOutcome RestoreFrozenDraft(
        AutomationRuntimeSerialization.PersistedFlow flow
    )
    {
        var nodes = ImmutableArray.CreateBuilder<AutomationFlowDraftNode>();
        foreach (var node in flow.Nodes)
        {
            if (
                AutomationRuntimeSerialization.RestoreInputBindings(node.InputBindingsJson)
                is not AutomationInputBindingsRestoreOutcome.Available bindings
            )
            {
                return new AutomationFlowDraftRestoreOutcome.Invalid();
            }

            nodes.Add(
                new(
                    new(node.Id),
                    AutomationRuntimeSerialization.Definition(node),
                    new(node.ExpressionLanguageVersion),
                    node.ContinueOnFailure
                        ? AutomationNodeFailurePolicy.Continue
                        : AutomationNodeFailurePolicy.Stop,
                    bindings.Bindings
                )
            );
        }

        return new AutomationFlowDraftRestoreOutcome.Available(
            new(
                new(flow.Id),
                new(flow.HostId),
                "Frozen automation",
                flow.SchemaVersion,
                true,
                nodes.ToImmutable(),
                flow.Edges.Select(static edge => new AutomationFlowDraftEdge(
                        edge.Id,
                        edge.Kind,
                        new(edge.SourceNodeId),
                        new(edge.SourcePortId),
                        new(edge.TargetNodeId),
                        new(edge.TargetPortId)
                    ))
                    .ToImmutableArray(),
                new(AutomationFlowOrientation.Horizontal, AutomationEdgeStyle.Angular)
            )
        );
    }

    private static PersistedAutomationEdgeKind Persist(AutomationEdgeKind kind) =>
        kind switch
        {
            AutomationEdgeKind.Flow => PersistedAutomationEdgeKind.Flow,
            AutomationEdgeKind.Data => PersistedAutomationEdgeKind.Data,
            _ => (PersistedAutomationEdgeKind)(-1),
        };

    private static AutomationEdgeKind Restore(PersistedAutomationEdgeKind kind) =>
        kind switch
        {
            PersistedAutomationEdgeKind.Flow => AutomationEdgeKind.Flow,
            PersistedAutomationEdgeKind.Data => AutomationEdgeKind.Data,
            _ => (AutomationEdgeKind)(-1),
        };

    private static AutomationGraphError MalformedGraphError() =>
        new(null, "graph-data-invalid", "Repair or remove this malformed automation flow.");

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

    private async Task<AutomationSampleRunOutcome> EvaluateSampleAsync(
        AutomationFlowDraft draft,
        AutomationNodeId sourceNodeId,
        AutomationContext context,
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
            var inputs = await catalog.Data.ResolveInputsAsync(
                draft.HostId,
                context,
                persisted,
                persistedNode,
                checkpoints,
                cancellationToken
            );
            if (inputs is not AutomationInputResolution.Available resolvedInputs)
            {
                outcomes.Add(
                    new(node.Id, AutomationNodeRunState.Failed, "input-resolution-failed")
                );
                return new AutomationSampleRunOutcome.Failed(outcomes.ToImmutable());
            }

            var evaluated = EvaluateSampleNode(valid.Configuration, context);
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

    private enum AutomationGraphAdmission
    {
        Saved,
        Frozen,
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
