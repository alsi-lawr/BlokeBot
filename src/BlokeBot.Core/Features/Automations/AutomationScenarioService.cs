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
        var traceId = Guid.NewGuid();
        await using var graphDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        var expansion = await AutomationFrozenSubflows.FreezeAsync(
            graphDb,
            AutomationScenarioGraph.FreezeGraph(configured),
            traceId,
            cancellationToken
        );
        if (expansion is null)
        {
            return new AutomationScenarioRunOutcome.Invalid([
                new(
                    null,
                    "subflow-unavailable",
                    "Restore the pinned subflow revisions before testing."
                ),
            ]);
        }
        var frozen = expansion.Flow;
        foreach (var node in frozen.Nodes)
        {
            if (
                AutomationFrozenSubflows.WithContract(
                    node,
                    catalog.ValidatePersistedDefinition(
                        AutomationRuntimeSerialization.Definition(node)
                    )
                )
                    is not AutomationConfigurationCheck.Valid valid
                || !AutomationScenarioSimulation.Supports(valid, catalog.Data)
            )
            {
                return new AutomationScenarioRunOutcome.Invalid([
                    new(
                        new(node.AuthorNodeId ?? node.Id),
                        "scenario-simulation-unsupported",
                        "This definition has no isolated simulation contract."
                    ),
                ]);
            }
        }
        var context = SourceContext(configured, fixture);
        var now = fixture.ClockUtc.UtcDateTime;
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
        RecordOutcome(
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
        var openInvocations = new HashSet<Guid>();
        await Schedule(fixture.SourceNodeId.Value, null, now);
        try
        {
            while (pending.Count > 0 || openInvocations.Count > 0)
            {
                var incompleteBoundary = !pending.TryDequeue(out var pendingNode);
                if (incompleteBoundary)
                {
                    var unfinished = frozen.Invocations.Last(invocation =>
                        openInvocations.Contains(invocation.EntryId)
                    );
                    pendingNode = new(unfinished.CallerId, now);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (pendingNode!.AvailableAtUtc > now)
                {
                    now = pendingNode.AvailableAtUtc;
                    await Emit(AutomationTraceEventKind.Resume);
                }
                var node = frozen.Nodes.Single(candidate => candidate.Id == pendingNode.Id);
                var check = AutomationFrozenSubflows.WithContract(
                    node,
                    catalog.ValidatePersistedDefinition(
                        AutomationRuntimeSerialization.Definition(node)
                    )
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
                var inputs = incompleteBoundary
                    ? new AutomationInputResolution.Failed("subflow-exit-not-reached")
                    : await catalog.Data.ResolveScenarioInputsAsync(
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
                if (
                    valid.Configuration is AutomationSubflowConfiguration
                    && inputs is AutomationInputResolution.Available boundaryInputs
                )
                {
                    var invocation =
                        node.DefinitionId == AutomationSubflowDefinitions.Invoke
                            ? frozen.Invocations.Single(value => value.CallerId == node.Id)
                            : frozen.Invocations.Single(value => value.ExitId == node.Id);
                    if (node.DefinitionId == AutomationSubflowDefinitions.Invoke)
                    {
                        var entry = frozen.Nodes.Single(value => value.Id == invocation.EntryId);
                        _ = openInvocations.Add(invocation.EntryId);
                        _ = scheduled.Add(entry.Id);
                        _ = await checkpoints.CompleteAsync(
                            entry,
                            boundaryInputs.PortValues,
                            cancellationToken
                        );
                        await Emit(
                            AutomationTraceEventKind.SubflowEntry,
                            entry,
                            AutomationTraceOutcome.Succeeded,
                            values: boundaryInputs.PortValues
                        );
                        await Schedule(entry.Id, "complete", now);
                    }
                    else
                    {
                        var caller = frozen.Nodes.Single(value => value.Id == invocation.CallerId);
                        RecordOutcome(
                            new(
                                new(caller.Id),
                                AutomationNodeRunState.Succeeded,
                                "subflow-completed",
                                [],
                                new(now, TimeSpan.Zero)
                            )
                        );
                        _ = await checkpoints.CompleteAsync(
                            caller,
                            boundaryInputs.PortValues,
                            cancellationToken
                        );
                        _ = openInvocations.Remove(invocation.EntryId);
                        await Emit(
                            AutomationTraceEventKind.SubflowExit,
                            node,
                            AutomationTraceOutcome.Succeeded,
                            values: boundaryInputs.PortValues
                        );
                        await Schedule(node.Id, "complete", now);
                    }
                    RecordOutcome(
                        new(
                            new(node.Id),
                            node.DefinitionId == AutomationSubflowDefinitions.Invoke
                                ? AutomationNodeRunState.Waiting
                                : AutomationNodeRunState.Succeeded,
                            "subflow-boundary",
                            [],
                            new(now, TimeSpan.Zero)
                        )
                    );
                    if (node.DefinitionId == AutomationSubflowDefinitions.Exit)
                    {
                        await Emit(
                            AutomationTraceEventKind.Result,
                            frozen.Nodes.Single(value => value.Id == invocation.CallerId),
                            AutomationTraceOutcome.Succeeded
                        );
                        await Emit(
                            AutomationTraceEventKind.Result,
                            node,
                            AutomationTraceOutcome.Succeeded
                        );
                    }
                    continue;
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
                    RecordOutcome(
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
                    if (
                        AutomationFrozenSubflows.Invocation(frozen, node.Id) is { } ownInvocation
                        && openInvocations.Remove(ownInvocation.EntryId)
                    )
                    {
                        await Emit(
                            AutomationTraceEventKind.SubflowExit,
                            frozen.Nodes.Single(candidate => candidate.Id == ownInvocation.ExitId),
                            AutomationTraceOutcome.Failed
                        );
                    }
                    var failureNode = node;
                    foreach (var invocation in AutomationFrozenSubflows.Unwind(frozen, node))
                    {
                        var dropped = pending
                            .Where(item =>
                                frozen.Nodes.Single(candidate => candidate.Id == item.Id).Invocation
                                    is { } nested
                                && frozen
                                    .Invocations.Single(candidate => candidate.EntryId == nested.Id)
                                    .Path.StartsWith(invocation.Path, StringComparison.Ordinal)
                            )
                            .Select(item => item.Id)
                            .ToHashSet();
                        var retained = pending.Where(item => !dropped.Contains(item.Id)).ToArray();
                        pending.Clear();
                        foreach (var item in retained)
                        {
                            pending.Enqueue(item);
                        }
                        await CloseOpen(
                            AutomationTraceOutcome.Failed,
                            cancellationToken,
                            invocation.Path
                        );
                        failureNode = frozen.Nodes.Single(candidate =>
                            candidate.Id == invocation.CallerId
                        );
                        RecordOutcome(
                            new(
                                new(failureNode.Id),
                                failureNode.ContinueOnFailure
                                    ? AutomationNodeRunState.ContinuedAfterFailure
                                    : AutomationNodeRunState.Failed,
                                "subflow-failed",
                                [],
                                new(now, TimeSpan.Zero)
                            )
                        );
                        await checkpoints.FailAsync(
                            failureNode,
                            "subflow-failed",
                            cancellationToken
                        );
                        await Emit(
                            AutomationTraceEventKind.Result,
                            failureNode,
                            failureNode.ContinueOnFailure
                                ? AutomationTraceOutcome.ContinuedAfterFailure
                                : AutomationTraceOutcome.Failed
                        );
                    }
                    if (!failureNode.ContinueOnFailure)
                    {
                        await CloseOpen(AutomationTraceOutcome.Failed, cancellationToken);
                        await Emit(
                            AutomationTraceEventKind.Terminal,
                            outcome: AutomationTraceOutcome.Failed
                        );
                        return new AutomationScenarioRunOutcome.Failed(
                            outcomes.ToImmutable(),
                            new(traceId)
                        );
                    }
                    await Schedule(
                        AutomationFrozenSubflows.Continuation(frozen, failureNode).Id,
                        "complete",
                        now
                    );
                }
                else if (execution is AutomationNodeExecution.Succeeded succeeded)
                {
                    RecordOutcome(
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
            await CloseOpen(AutomationTraceOutcome.Cancelled, CancellationToken.None);
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

        void RecordOutcome(AutomationScenarioNodeOutcome outcome)
        {
            var node = frozen.Nodes.Single(value => value.Id == outcome.NodeId.Value);
            outcome = outcome with
            {
                AuthorNodeId = node.AuthorNodeId is { } author ? new(author) : null,
                Invocation = node.Invocation is { } nested
                    ? AutomationFrozenSubflows.Trace(nested, traceId)
                    : new(traceId),
            };
            for (var index = 0; index < outcomes.Count; index++)
            {
                if (outcomes[index].NodeId == outcome.NodeId)
                {
                    outcomes[index] = outcome;
                    return;
                }
            }
            outcomes.Add(outcome);
        }

        async Task CloseOpen(
            AutomationTraceOutcome outcome,
            CancellationToken token,
            string? path = null
        )
        {
            if (frozen.Invocations.IsDefault)
            {
                return;
            }
            foreach (
                var invocation in frozen
                    .Invocations.Reverse()
                    .Where(invocation =>
                        openInvocations.Contains(invocation.EntryId)
                        && (
                            path is null
                            || invocation.Path.StartsWith(path, StringComparison.Ordinal)
                        )
                    )
            )
            {
                _ = openInvocations.Remove(invocation.EntryId);
                RecordOutcome(
                    new(
                        new(invocation.CallerId),
                        AutomationNodeRunState.Failed,
                        "subflow-terminated",
                        [],
                        new(now, TimeSpan.Zero)
                    )
                );
                await Emit(
                    AutomationTraceEventKind.SubflowExit,
                    frozen.Nodes.Single(node => node.Id == invocation.ExitId),
                    outcome,
                    token: token
                );
            }
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
