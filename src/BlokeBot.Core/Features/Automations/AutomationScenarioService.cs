using System.Collections.Immutable;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationScenarioService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationCatalogService catalog,
    AutomationFlowService flows,
    TimeProvider clock
)
{
    public const int MaximumScenariosPerFlow = 32;
    public const int MaximumFixtureBytes = 65_536;
    public const int MaximumGraphNodes = 256;
    public const int MaximumGraphEdges = 1024;

    public async Task<AutomationScenarioRunOutcome> RunAsync(
        AutomationFlowDraft draft,
        AutomationScenarioFixture fixture,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await ValidateAsync(draft, fixture, cancellationToken);
        if (validation.Gate == AutomationCatalogAvailability.Disabled)
        {
            return new AutomationScenarioRunOutcome.FeatureDisabled();
        }
        if (validation.Gate == AutomationCatalogAvailability.HostNotFound)
        {
            return new AutomationScenarioRunOutcome.HostNotFound();
        }
        if (!validation.Errors.IsEmpty)
        {
            return new AutomationScenarioRunOutcome.Invalid(validation.Errors);
        }
        var configured = ApplyConfigurations(draft, fixture);
        var frozen = AutomationScenarioGraph.FreezeGraph(configured);
        var context = SourceContext(configured, fixture);
        var now = fixture.ClockUtc.UtcDateTime;
        var traceId = Guid.NewGuid();
        await using var traceDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        await AutomationTraceStore.CreateAsync(
            traceDb,
            traceId,
            draft.HostId.Value,
            draft.Id?.Value,
            null,
            clock.GetUtcNow().UtcDateTime,
            cancellationToken
        );
        Task Emit(
            AutomationTraceEventKind kind,
            AutomationRuntimeSerialization.PersistedNode? node = null,
            AutomationTraceOutcome outcome = AutomationTraceOutcome.None,
            string? port = null,
            DateTime? due = null,
            IReadOnlyDictionary<AutomationPortId, AutomationResolvedValue>? values = null,
            CancellationToken? token = null
        ) =>
            AutomationTraceStore.AppendAsync(
                traceDb,
                traceId,
                AutomationTraceRedaction.Event(
                    traceId,
                    kind,
                    now,
                    node,
                    outcome,
                    port,
                    due,
                    values
                ),
                clock.GetUtcNow().UtcDateTime,
                token ?? cancellationToken
            );
        await Emit(
            AutomationTraceEventKind.Admission,
            frozen.Nodes.Single(node => node.Id == fixture.SourceNodeId.Value),
            AutomationTraceOutcome.Succeeded
        );
        await Emit(AutomationTraceEventKind.Resume);
        var outcomes = ImmutableArray.CreateBuilder<AutomationScenarioNodeOutcome>();
        outcomes.Add(
            new(
                fixture.SourceNodeId,
                AutomationNodeRunState.Succeeded,
                "source-received",
                [],
                new(now, TimeSpan.Zero)
            )
        );
        var checkpoints = new AutomationScenarioCheckpointStore(
            (node, kind, outcome, values, token) =>
                Emit(kind, node, outcome, values: values, token: token)
        );
        var entropy = new AutomationSeededIntegerEntropy(fixture.Seed);
        var pending = new Queue<PendingNode>();
        var scheduled = new HashSet<Guid> { fixture.SourceNodeId.Value };
        await Schedule(fixture.SourceNodeId.Value, null, now);
        try
        {
            while (pending.TryDequeue(out var pendingNode))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pendingNode.AvailableAtUtc > now)
                {
                    now = pendingNode.AvailableAtUtc;
                    await Emit(AutomationTraceEventKind.Resume);
                }
                var node = frozen.Nodes.Single(candidate => candidate.Id == pendingNode.Id);
                var check = catalog.ValidatePersistedDefinition(
                    AutomationRuntimeSerialization.Definition(node)
                );
                if (check is not AutomationConfigurationCheck.Valid valid)
                {
                    await Emit(
                        AutomationTraceEventKind.Terminal,
                        outcome: AutomationTraceOutcome.Failed
                    );
                    return new AutomationScenarioRunOutcome.Invalid([
                        new(
                            new(node.Id),
                            "definition-unavailable",
                            "Restore this node definition before testing."
                        ),
                    ]);
                }
                var inputs = await catalog.Data.ResolveScenarioInputsAsync(
                    draft.HostId,
                    context,
                    frozen,
                    node,
                    checkpoints,
                    entropy,
                    fixture.ConnectedInputs,
                    cancellationToken
                );
                cancellationToken.ThrowIfCancellationRequested();
                if (inputs is AutomationInputResolution.Available traceInputs)
                {
                    await Emit(
                        AutomationTraceEventKind.ResolvedInputs,
                        node,
                        values: traceInputs.PortValues
                    );
                    await Emit(AutomationTraceEventKind.Attempt, node);
                }
                var execution = inputs is AutomationInputResolution.Available available
                    ? AutomationScenarioSimulation.Evaluate(
                        valid.Configuration,
                        available.FieldValues,
                        fixture
                            .Effects.FirstOrDefault(effect => effect.NodeId.Value == node.Id)
                            ?.Result
                            ?? AutomationScenarioEffectResult.Succeeded,
                        now
                    )
                    : new AutomationNodeExecution.Failed(
                        ((AutomationInputResolution.Failed)inputs).Code
                    );
                var diagnostics = inputs is AutomationInputResolution.Available resolved
                    ? AutomationDataValueSerialization.Diagnostics(resolved.PortValues)
                    : [];
                if (execution is AutomationNodeExecution.Failed failed)
                {
                    await Emit(
                        AutomationTraceEventKind.Result,
                        node,
                        node.ContinueOnFailure
                            ? AutomationTraceOutcome.ContinuedAfterFailure
                            : AutomationTraceOutcome.Failed
                    );
                    outcomes.Add(
                        new(
                            new(node.Id),
                            node.ContinueOnFailure
                                ? AutomationNodeRunState.ContinuedAfterFailure
                                : AutomationNodeRunState.Failed,
                            failed.Code,
                            diagnostics,
                            new(now, TimeSpan.Zero)
                        )
                    );
                    if (!node.ContinueOnFailure)
                    {
                        await Emit(
                            AutomationTraceEventKind.Terminal,
                            outcome: AutomationTraceOutcome.Failed
                        );
                        return new AutomationScenarioRunOutcome.Failed(
                            outcomes.ToImmutable(),
                            new(traceId)
                        );
                    }
                    await Schedule(node.Id, "complete", now);
                }
                else if (execution is AutomationNodeExecution.Succeeded succeeded)
                {
                    outcomes.Add(
                        new(
                            new(node.Id),
                            AutomationNodeRunState.Succeeded,
                            succeeded.Code,
                            diagnostics,
                            new(now, TimeSpan.Zero)
                        )
                    );
                    await Emit(
                        AutomationTraceEventKind.Result,
                        node,
                        AutomationTraceOutcome.Succeeded
                    );
                    await Emit(AutomationTraceEventKind.Branch, node, port: succeeded.OutputPort);
                    if (
                        node.DefinitionId == AutomationDefinitionIds.DelayControl.Value
                        || succeeded.NextAvailableAtUtc > now
                    )
                    {
                        await Emit(
                            AutomationTraceEventKind.Delay,
                            node,
                            due: succeeded.NextAvailableAtUtc
                        );
                    }
                    await Schedule(node.Id, succeeded.OutputPort, succeeded.NextAvailableAtUtc);
                }
            }
            await Emit(
                AutomationTraceEventKind.Terminal,
                outcome: AutomationTraceOutcome.Succeeded
            );
            return new AutomationScenarioRunOutcome.Completed(outcomes.ToImmutable(), new(traceId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            traceDb.ChangeTracker.Clear();
            await Emit(
                AutomationTraceEventKind.Cancellation,
                outcome: AutomationTraceOutcome.Cancelled,
                token: CancellationToken.None
            );
            await Emit(
                AutomationTraceEventKind.Terminal,
                outcome: AutomationTraceOutcome.Cancelled,
                token: CancellationToken.None
            );
            throw;
        }

        async Task Schedule(Guid nodeId, string? port, DateTime due)
        {
            foreach (
                var target in AutomationFlowTraversal.Schedule(
                    frozen.Edges,
                    nodeId,
                    port,
                    scheduled
                )
            )
            {
                pending.Enqueue(new(target, due));
                await Emit(
                    AutomationTraceEventKind.Scheduled,
                    frozen.Nodes.Single(node => node.Id == target),
                    due: due
                );
            }
        }
    }

    private static AutomationFlowDraft ApplyConfigurations(
        AutomationFlowDraft draft,
        AutomationScenarioFixture fixture
    ) =>
        draft with
        {
            Nodes =
            [
                .. draft.Nodes.Select(node =>
                    fixture.Configurations.FirstOrDefault(value => value.NodeId == node.Id)
                        is { } replacement
                        ? node with
                        {
                            Definition = replacement.Definition,
                        }
                        : node
                ),
            ],
        };

    private sealed record PendingNode(Guid Id, DateTime AvailableAtUtc);
}
