using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal sealed record AutomationFlowAuthoringEntry(
    AutomationFlowId Id,
    string Name,
    bool IsEnabled,
    bool RequiresJson,
    string Reason
);

internal abstract record AutomationFlowAuthoringReadOutcome
{
    private AutomationFlowAuthoringReadOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Editor, TResult> editor,
        Func<Json, TResult> json,
        Func<Unavailable, TResult> unavailable
    );

    internal sealed record Editor(
        AutomationFlowSnapshot Snapshot,
        AutomationAuthoredFlowSource Original,
        ImmutableArray<AutomationGraphError> Errors
    ) : AutomationFlowAuthoringReadOutcome
    {
        internal override TResult Match<TResult>(
            Func<Editor, TResult> editor,
            Func<Json, TResult> json,
            Func<Unavailable, TResult> unavailable
        ) => editor(this);
    }

    internal sealed record Json(
        AutomationFlowId Id,
        AutomationAuthoredFlowSource Original,
        bool IsEnabled,
        ImmutableArray<AutomationGraphError> Errors
    ) : AutomationFlowAuthoringReadOutcome
    {
        internal override TResult Match<TResult>(
            Func<Editor, TResult> editor,
            Func<Json, TResult> json,
            Func<Unavailable, TResult> unavailable
        ) => json(this);
    }

    internal sealed record Unavailable : AutomationFlowAuthoringReadOutcome
    {
        internal override TResult Match<TResult>(
            Func<Editor, TResult> editor,
            Func<Json, TResult> json,
            Func<Unavailable, TResult> unavailable
        ) => unavailable(this);
    }
}

internal abstract record AutomationFlowSourceValidationOutcome
{
    private AutomationFlowSourceValidationOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Valid, TResult> valid,
        Func<Invalid, TResult> invalid,
        Func<Unavailable, TResult> unavailable
    );

    internal sealed record Valid(AutomationFlowDraft Draft, AutomationAuthoredFlowSource Source)
        : AutomationFlowSourceValidationOutcome
    {
        internal override TResult Match<TResult>(
            Func<Valid, TResult> valid,
            Func<Invalid, TResult> invalid,
            Func<Unavailable, TResult> unavailable
        ) => valid(this);
    }

    internal sealed record Invalid(ImmutableArray<AutomationFlowSourceError> Errors)
        : AutomationFlowSourceValidationOutcome
    {
        internal override TResult Match<TResult>(
            Func<Valid, TResult> valid,
            Func<Invalid, TResult> invalid,
            Func<Unavailable, TResult> unavailable
        ) => invalid(this);
    }

    internal sealed record Unavailable : AutomationFlowSourceValidationOutcome
    {
        internal override TResult Match<TResult>(
            Func<Valid, TResult> valid,
            Func<Invalid, TResult> invalid,
            Func<Unavailable, TResult> unavailable
        ) => unavailable(this);
    }
}

public sealed partial class AutomationFlowService
{
    internal async Task<AutomationFlowAuthoringReadOutcome> ReadForAuthoringAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        CancellationToken cancellationToken
    )
    {
        if (
            (await catalog.DiscoverAsync(hostId, cancellationToken)).Availability
            != AutomationCatalogAvailability.Enabled
        )
        {
            return new AutomationFlowAuthoringReadOutcome.Unavailable();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var flow = await db
            .AutomationFlows.AsNoTracking()
            .Include(value => value.Nodes)
            .Include(value => value.Edges)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId.Value && value.Id == flowId.Value,
                cancellationToken
            );
        return flow is null
            ? new AutomationFlowAuthoringReadOutcome.Unavailable()
            : await AuthoringReadAsync(flow, cancellationToken);
    }

    private async Task<AutomationFlowAuthoringReadOutcome> AuthoringReadAsync(
        AutomationFlow flow,
        CancellationToken cancellationToken
    )
    {
        var original = AuthoredSource(flow);
        if (
            flow.Nodes.Any(node =>
                AutomationAuthoredFlowCodec.ReadConfiguration(
                    node.ConfigurationJson,
                    "$.nodes.configurationJson"
                )
                    is not null
                || AutomationAuthoredFlowCodec.ReadBindings(
                    node.InputBindingsJson,
                    new(node.DefinitionId),
                    "$.nodes.inputBindingsJson",
                    forAuthoring: true
                )
                    is not null
            )
            || RestoreDraft(flow, forAuthoring: true)
                is not AutomationFlowDraftRestoreOutcome.Available restored
        )
        {
            return new AutomationFlowAuthoringReadOutcome.Json(
                new(flow.Id),
                original,
                flow.IsEnabled,
                [MalformedGraphError()]
            );
        }

        var draft = restored.Draft;
        if (
            draft.SchemaVersion != AutomationFlowSchema.CurrentVersion
            || draft.Nodes.Select(node => node.Id).Distinct().Count() != draft.Nodes.Length
            || draft.Nodes.Any(node =>
                node.Id.Value == Guid.Empty
                || !Enum.IsDefined(node.FailurePolicy)
                || catalog.DescribeForAuthoring(draft.HostId, node)
                    is not AutomationConfigurationCheck.Valid
            )
            || draft.Edges.Any(edge => !Enum.IsDefined(edge.Kind))
        )
        {
            return new AutomationFlowAuthoringReadOutcome.Json(
                new(flow.Id),
                original,
                flow.IsEnabled,
                [MalformedGraphError()]
            );
        }

        var validation = await ValidateAsync(draft, cancellationToken);
        return new AutomationFlowAuthoringReadOutcome.Editor(
            new(
                draft,
                new(flow.CreatedAtUtc, TimeSpan.Zero),
                new(flow.UpdatedAtUtc, TimeSpan.Zero),
                flow.UnavailableReason
            ),
            original,
            validation.Errors
        );
    }

    private static AutomationAuthoredFlowSource AuthoredSource(AutomationFlow flow) =>
        new(
            flow.Name,
            flow.SchemaVersion,
            new(
                flow.UseVerticalLayout
                    ? AutomationFlowOrientation.Vertical
                    : AutomationFlowOrientation.Horizontal,
                flow.UseSmoothEdges ? AutomationEdgeStyle.Smooth : AutomationEdgeStyle.Angular
            ),
            [
                .. flow.Nodes.Select(node => new AutomationAuthoredFlowNode(
                    node.Id,
                    node.DefinitionId,
                    node.DefinitionSchemaVersion,
                    node.ConfigurationJson,
                    node.InputBindingsJson,
                    node.ExpressionLanguageVersion,
                    node.ContinueOnFailure
                        ? AutomationNodeFailurePolicy.Continue
                        : AutomationNodeFailurePolicy.Stop,
                    new(node.CanvasX, node.CanvasY),
                    node.DisplayAlias
                )),
            ],
            [
                .. flow.Edges.Select(edge => new AutomationAuthoredFlowEdge(
                    edge.Id,
                    Restore(edge.Kind),
                    edge.SourceNodeId,
                    edge.SourcePortId,
                    edge.TargetNodeId,
                    edge.TargetPortId
                )),
            ]
        );

    internal async Task<AutomationFlowSourceValidationOutcome> ValidateSourceAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        string text,
        CancellationToken cancellationToken
    ) =>
        (await catalog.DiscoverAsync(hostId, cancellationToken)).Availability
        != AutomationCatalogAvailability.Enabled
            ? new AutomationFlowSourceValidationOutcome.Unavailable()
            : await AutomationAuthoredFlowCodec
                .Parse(text)
                .Match(
                    parsed =>
                        ValidateParsedSourceAsync(hostId, flowId, parsed.Source, cancellationToken),
                    invalid =>
                        Task.FromResult<AutomationFlowSourceValidationOutcome>(
                            new AutomationFlowSourceValidationOutcome.Invalid(invalid.Errors)
                        )
                );

    private async Task<AutomationFlowSourceValidationOutcome> ValidateParsedSourceAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        AutomationAuthoredFlowSource source,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db
            .AutomationFlows.AsNoTracking()
            .Include(flow => flow.Nodes)
            .SingleOrDefaultAsync(
                flow => flow.HostId == hostId.Value && flow.Id == flowId.Value,
                cancellationToken
            );
        if (existing is null)
        {
            return new AutomationFlowSourceValidationOutcome.Unavailable();
        }

        var built = await SourceDraftAsync(existing, source, cancellationToken);
        return await built.Match<Task<AutomationFlowSourceValidationOutcome>>(
            async valid =>
            {
                var validation = await ValidateAsync(valid.Draft, cancellationToken);
                return validation.Gate is not null
                        ? new AutomationFlowSourceValidationOutcome.Unavailable()
                    : !validation.Errors.IsEmpty
                        ? new AutomationFlowSourceValidationOutcome.Invalid(
                            SourceErrors(validation.Errors)
                        )
                    : valid;
            },
            Task.FromResult<AutomationFlowSourceValidationOutcome>,
            Task.FromResult<AutomationFlowSourceValidationOutcome>
        );
    }

    internal async Task<AutomationFlowSaveOutcome> SaveSourceAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        string text,
        CancellationToken cancellationToken
    ) =>
        await (await ValidateSourceAsync(hostId, flowId, text, cancellationToken)).Match<
            Task<AutomationFlowSaveOutcome>
        >(
            valid => SaveCoreAsync(valid.Draft, valid.Source, cancellationToken),
            invalid =>
                Task.FromResult<AutomationFlowSaveOutcome>(
                    new AutomationFlowSaveOutcome.Invalid([
                        .. invalid.Errors.Select(error => new AutomationGraphError(
                            null,
                            "source-invalid",
                            error.Path + ": " + error.Message
                        )),
                    ])
                ),
            _ =>
                Task.FromResult<AutomationFlowSaveOutcome>(
                    new AutomationFlowSaveOutcome.FlowNotFound()
                )
        );

    private async Task<AutomationFlowSourceValidationOutcome> SourceDraftAsync(
        AutomationFlow existing,
        AutomationAuthoredFlowSource source,
        CancellationToken cancellationToken
    )
    {
        var availability = await catalog.DiscoverAsync(new(existing.HostId), cancellationToken);
        var descriptors = availability.Definitions.ToDictionary(value => value.Id);
        var nodes = ImmutableArray.CreateBuilder<AutomationFlowDraftNode>();
        foreach (var node in source.Nodes)
        {
            var old = existing.Nodes.SingleOrDefault(value =>
                value.Id == node.Id && value.DefinitionId == node.DefinitionId
            );
            AutomationPluginProvenance? provenance;
            if (old is not null)
            {
                if (
                    old.PluginProvenanceJson is not null
                    && !PluginAutomationCatalogRegistry.TryDeserializeProvenance(
                        old.PluginProvenanceJson,
                        out _
                    )
                )
                {
                    return SourceInvalid(
                        "$.nodes",
                        "The stored node provider identity is invalid; it cannot be replaced by editing source."
                    );
                }

                provenance = PluginAutomationCatalogRegistry.TryDeserializeProvenance(
                    old.PluginProvenanceJson,
                    out var retained
                )
                    ? retained
                    : null;
            }
            else if (descriptors.TryGetValue(new(node.DefinitionId), out var descriptor))
            {
                provenance = descriptor.PluginProvenance;
            }
            else
            {
                return SourceInvalid(
                    "$.nodes",
                    "Restore the unavailable node provider before saving."
                );
            }

            using var configuration = JsonDocument.Parse(node.ConfigurationJson);
            var bindings = (AutomationInputBindingsRestoreOutcome.Available)
                AutomationRuntimeSerialization.RestoreInputBindings(
                    node.InputBindingsJson,
                    new(node.DefinitionId)
                );
            nodes.Add(
                new(
                    new(node.Id),
                    new(
                        node.DefinitionId,
                        node.DefinitionSchemaVersion,
                        configuration.RootElement.Clone(),
                        provenance
                    ),
                    new(node.ExpressionLanguageVersion),
                    node.FailurePolicy,
                    bindings.Bindings,
                    new(new(node.Position.X), new(node.Position.Y)),
                    node.DisplayAlias
                )
            );
        }
        return new AutomationFlowSourceValidationOutcome.Valid(
            new(
                new(existing.Id),
                new(existing.HostId),
                source.Name,
                source.SchemaVersion,
                existing.IsEnabled,
                nodes.ToImmutable(),
                [
                    .. source.Edges.Select(edge => new AutomationFlowDraftEdge(
                        edge.Id,
                        edge.Kind,
                        new(edge.SourceNodeId),
                        new(edge.SourcePortId),
                        new(edge.TargetNodeId),
                        new(edge.TargetPortId)
                    )),
                ],
                source.Canvas
            ),
            source
        );
    }

    private static bool SourcePayloadMatches(
        AutomationFlowDraft draft,
        AutomationAuthoredFlowSource source
    )
    {
        if (
            draft.Name != source.Name
            || draft.SchemaVersion != source.SchemaVersion
            || draft.Canvas != source.Canvas
            || draft.Nodes.Length != source.Nodes.Length
            || draft.Edges.Length != source.Edges.Length
        )
        {
            return false;
        }

        for (var index = 0; index < draft.Nodes.Length; index++)
        {
            var node = draft.Nodes[index];
            var authored = source.Nodes[index];
            using var configuration = JsonDocument.Parse(authored.ConfigurationJson);
            if (
                node.Id.Value != authored.Id
                || node.Definition.TypeId != authored.DefinitionId
                || node.Definition.SchemaVersion != authored.DefinitionSchemaVersion
                || node.ExpressionLanguageVersion.Value != authored.ExpressionLanguageVersion
                || node.FailurePolicy != authored.FailurePolicy
                || node.DisplayAlias != authored.DisplayAlias
                || node.Position.X.Value != authored.Position.X
                || node.Position.Y.Value != authored.Position.Y
                || !JsonElement.DeepEquals(node.Definition.Configuration, configuration.RootElement)
                || AutomationRuntimeSerialization.RestoreInputBindings(
                    authored.InputBindingsJson,
                    new(authored.DefinitionId)
                )
                    is not AutomationInputBindingsRestoreOutcome.Available bindings
                || node.InputBindings.Count != bindings.Bindings.Count
                || !node.InputBindings.All(pair =>
                    bindings.Bindings.TryGetValue(pair.Key, out var value) && value == pair.Value
                )
            )
            {
                return false;
            }
        }
        for (var index = 0; index < draft.Edges.Length; index++)
        {
            var edge = draft.Edges[index];
            var authored = source.Edges[index];
            if (
                edge.Id != authored.Id
                || edge.Kind != authored.Kind
                || edge.SourceNodeId.Value != authored.SourceNodeId
                || edge.TargetNodeId.Value != authored.TargetNodeId
                || edge.SourcePortId.Value != authored.SourcePortId
                || edge.TargetPortId.Value != authored.TargetPortId
            )
            {
                return false;
            }
        }
        return true;
    }

    private static ImmutableArray<AutomationFlowSourceError> SourceErrors(
        ImmutableArray<AutomationGraphError> errors
    ) =>
        [
            .. errors.Select(error => new AutomationFlowSourceError(
                error.NodeId is { } id ? $"$.nodes[id={id.Value}]" : "$",
                error.Message
            )),
        ];

    private static AutomationFlowSourceValidationOutcome SourceInvalid(
        string path,
        string message
    ) => new AutomationFlowSourceValidationOutcome.Invalid([new(path, message)]);
}
