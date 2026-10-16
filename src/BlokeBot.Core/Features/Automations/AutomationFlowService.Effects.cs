using System.Collections.Immutable;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    internal async Task<AutomationEffectDisclosure> EffectsAsync(
        BlokeBotDbContext db,
        AutomationFlowDraft graph,
        CancellationToken cancellationToken,
        IReadOnlyCollection<AutomationSubflowRevision>? prepared = null
    )
    {
        var loaded = await AutomationSubflowStore.LoadClosureAsync(
            db,
            graph.HostId,
            AutomationSubflowStore.Calls(graph.Nodes).Select(call => call.SubflowId),
            null,
            cancellationToken,
            prepared
        );
        if (loaded is AutomationSubflowClosureOutcome.Invalid invalid)
        {
            return AutomationEffectDisclosure.Unavailable(invalid.Errors);
        }
        var closure = ((AutomationSubflowClosureOutcome.Available)loaded).Closure;
        var summary = new AutomationEffectSummary(AutomationActionCapabilities.None, false);
        var pending = new Queue<AutomationFlowDraft>();
        var visited = new HashSet<AutomationSubflowId>();
        pending.Enqueue(graph);
        while (pending.TryDequeue(out var current))
        {
            var errors = AutomationSubflowStore.ValidateCalls(current, closure);
            if (!errors.IsEmpty)
            {
                return AutomationEffectDisclosure.Unavailable(errors);
            }
            var definitions = new Dictionary<AutomationNodeId, AutomationDefinitionDescriptor>();
            foreach (var node in current.Nodes)
            {
                if (
                    catalog.ValidatePreparedDefinition(
                        graph.HostId,
                        node.Definition,
                        node.InputBindings
                    )
                    is not AutomationConfigurationCheck.Valid valid
                )
                {
                    return AutomationEffectDisclosure.Unavailable([
                        new(
                            node.Id,
                            "effect-definition-unavailable",
                            "Restore this node type before reviewing its effects."
                        ),
                    ]);
                }
                definitions.Add(node.Id, valid.Definition);
            }
            var flow = current.Nodes.ToDictionary(
                node => node.Id,
                _ => new List<AutomationNodeId>()
            );
            var data = current.Nodes.ToDictionary(
                node => node.Id,
                _ => new List<AutomationNodeId>()
            );
            foreach (var edge in current.Edges)
            {
                if (edge.Kind == AutomationEdgeKind.Flow)
                {
                    flow[edge.SourceNodeId].Add(edge.TargetNodeId);
                }
                else if (
                    definitions[edge.TargetNodeId]
                        .Inputs.FirstOrDefault(port => port.Id == edge.TargetPortId)
                        ?.BindingFieldId
                        is { } field
                    && current
                        .Nodes.First(node => node.Id == edge.TargetNodeId)
                        .InputBindings.GetValueOrDefault(field)
                        ?.Mode == AutomationInputBindingMode.Connected
                )
                {
                    data[edge.TargetNodeId].Add(edge.SourceNodeId);
                }
            }
            var used = Reachable(
                current
                    .Nodes.Where(node =>
                        definitions[node.Id].Kind == AutomationNodeKind.Source
                        || node.Definition.TypeId == AutomationSubflowDefinitions.Entry
                    )
                    .Select(node => node.Id),
                flow
            );
            used.UnionWith(Reachable(used.ToArray(), data));
            foreach (
                var call in AutomationSubflowStore.Calls(
                    current.Nodes.Where(node => used.Contains(node.Id))
                )
            )
            {
                if (visited.Add(call.SubflowId))
                {
                    pending.Enqueue(
                        closure
                            .Revisions.Single(revision => revision.SubflowId == call.SubflowId)
                            .Graph
                    );
                }
            }
            foreach (var node in current.Nodes.Where(node => used.Contains(node.Id)))
            {
                var definition = definitions[node.Id];
                summary = new(
                    summary.Capabilities | definition.Capabilities,
                    summary.RunsPluginScripts || definition.PluginProvenance is not null
                );
            }
        }
        return AutomationEffectDisclosure.Complete(summary);
    }

    internal async Task<ImmutableArray<AutomationEffectParent>> EffectParentsAsync(
        BlokeBotDbContext db,
        AutomationHostId host,
        IReadOnlyCollection<AutomationSubflowRevision> prepared,
        CancellationToken cancellationToken,
        IReadOnlyCollection<AutomationFlowDraft>? finalFlows = null,
        IReadOnlySet<AutomationFlowId>? removedFlows = null
    )
    {
        var ancestors = prepared.Select(revision => revision.SubflowId.Value).ToHashSet();
        var nested = await db
            .AutomationSubflowNestedCallers.AsNoTracking()
            .Where(row => row.HostId == host.Value)
            .ToArrayAsync(cancellationToken);
        bool changed;
        do
        {
            changed = false;
            foreach (var caller in nested.Where(row => ancestors.Contains(row.SubflowId)))
            {
                changed |= ancestors.Add(caller.CallerSubflowId);
            }
        } while (changed);
        var ids = await (
            from reference in db.AutomationSubflowCallers.AsNoTracking()
            join node in db.AutomationFlowNodes.AsNoTracking() on reference.NodeId equals node.Id
            where reference.HostId == host.Value && ancestors.Contains(reference.SubflowId)
            select node.FlowId
        )
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var parents = await db
            .AutomationFlows.AsNoTracking()
            .Include(flow => flow.Nodes)
            .Include(flow => flow.Edges)
            .Where(flow => flow.HostId == host.Value && ids.Contains(flow.Id))
            .ToArrayAsync(cancellationToken);
        var result = ImmutableArray.CreateBuilder<AutomationEffectParent>();
        foreach (var parent in parents)
        {
            var id = new AutomationFlowId(parent.Id);
            if (removedFlows?.Contains(id) is true)
            {
                continue;
            }
            var final = finalFlows?.FirstOrDefault(flow => flow.Id == id);
            if (RestoreDraft(parent) is not AutomationFlowDraftRestoreOutcome.Available restored)
            {
                var unavailable = AutomationEffectDisclosure.Unavailable([MalformedGraphError()]);
                result.Add(new(id, parent.Name, parent.IsEnabled, unavailable, unavailable));
                continue;
            }
            var before = await EffectsAsync(db, restored.Draft, cancellationToken);
            var after = await EffectsAsync(
                db,
                final ?? restored.Draft,
                cancellationToken,
                prepared
            );
            result.Add(new(id, final?.Name ?? parent.Name, parent.IsEnabled, before, after));
        }
        return result.ToImmutable();
    }

    internal static bool SameEffects(
        AutomationEffectDisclosure left,
        AutomationEffectDisclosure right
    ) =>
        left.Summary == right.Summary
        && left.Errors.Length == right.Errors.Length
        && left.Errors.All(error =>
            right.Errors.Any(current =>
                current.NodeId == error.NodeId
                && current.Code == error.Code
                && current.FieldId == error.FieldId
                && current.PortId == error.PortId
            )
        );

    internal static bool SameEffectParents(
        ImmutableArray<AutomationEffectParent> left,
        ImmutableArray<AutomationEffectParent> right
    ) =>
        left.Length == right.Length
        && left.All(parent =>
            right.Any(current =>
                current.FlowId == parent.FlowId
                && current.IsEnabled == parent.IsEnabled
                && SameEffects(current.Before, parent.Before)
                && SameEffects(current.After, parent.After)
            )
        );
}

public sealed record AutomationEffectSummary(
    AutomationActionCapabilities Capabilities,
    bool RunsPluginScripts
)
{
    public bool IsEmpty => Capabilities == AutomationActionCapabilities.None && !RunsPluginScripts;

    internal bool AddsTo(AutomationEffectSummary previous) =>
        (Capabilities & ~previous.Capabilities) != AutomationActionCapabilities.None
        || (RunsPluginScripts && !previous.RunsPluginScripts);

    public string Description
    {
        get
        {
            var effects = new List<string>();
            if (Capabilities.HasFlag(AutomationActionCapabilities.SendsChat))
            {
                effects.Add("send public chat messages");
            }
            if (Capabilities.HasFlag(AutomationActionCapabilities.PlaysOverlays))
            {
                effects.Add("play overlay cues");
            }
            if (Capabilities.HasFlag(AutomationActionCapabilities.ChangesPoints))
            {
                effects.Add("change channel points");
            }
            if (Capabilities.HasFlag(AutomationActionCapabilities.CallsTwitchApi))
            {
                effects.Add("call Twitch");
            }
            if (RunsPluginScripts || Capabilities.HasFlag(AutomationActionCapabilities.RunsScripts))
            {
                effects.Add("run plugin scripts");
            }
            return effects.Count == 0 ? "No live effects." : string.Join(", ", effects);
        }
    }
}

public sealed record AutomationEffectDisclosure
{
    private AutomationEffectDisclosure(
        AutomationEffectSummary? summary,
        ImmutableArray<AutomationGraphError> errors
    )
    {
        Summary = summary;
        Errors = errors;
    }

    public AutomationEffectSummary? Summary { get; }
    public ImmutableArray<AutomationGraphError> Errors { get; }

    internal static AutomationEffectDisclosure Complete(AutomationEffectSummary summary) =>
        new(summary, []);

    internal static AutomationEffectDisclosure Unavailable(
        ImmutableArray<AutomationGraphError> errors
    ) => new(null, errors);
}

public sealed record AutomationEffectParent(
    AutomationFlowId FlowId,
    string Name,
    bool IsEnabled,
    AutomationEffectDisclosure Before,
    AutomationEffectDisclosure After
)
{
    public bool WillDisable =>
        IsEnabled
        && (
            Before.Summary is not { } before
            || After.Summary is not { } after
            || after.AddsTo(before)
        );
}

public sealed class AutomationFlowEnableReview
{
    internal AutomationFlowEnableReview(
        AutomationFlowDraft draft,
        AutomationEffectSummary effects,
        ImmutableDictionary<AutomationSubflowId, AutomationSubflowRevisionId> dependencies
    )
    {
        Draft = draft;
        Effects = effects;
        Dependencies = dependencies;
    }

    internal AutomationFlowDraft Draft { get; }
    public AutomationEffectSummary Effects { get; }
    internal ImmutableDictionary<
        AutomationSubflowId,
        AutomationSubflowRevisionId
    > Dependencies { get; }
}
