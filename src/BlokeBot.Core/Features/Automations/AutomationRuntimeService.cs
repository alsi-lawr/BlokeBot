using System.Collections.Immutable;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationCatalogService catalog,
    AutomationFlowService flowService,
    AutomationActionExecutor actions,
    TimeProvider clock,
    IAutomationUiChangePublisher uiChanges,
    ILogger<AutomationRuntimeService> logger,
    IEnumerable<IAutomationRunCompletionObserver>? runCompletionObservers = null
) : IPluginAutomationRunDispatcher
{
    private void ReportAdmissionRejection(
        AutomationHostId hostId,
        AutomationAdmissionRejection rejection
    )
    {
        foreach (var source in rejection.SourceNodeIds)
        {
            logger.LogWarning(
                "Automation admission rejected for host {HostId}, flow {FlowId}, source {SourceNodeId}: {Reason}",
                hostId.Value,
                rejection.FlowId.Value,
                source.Value,
                rejection.Reason
            );
        }
    }

    private readonly Lock _initializationGate = new();
    private readonly IAutomationRunCompletionObserver[] _runCompletionObservers =
    [
        .. runCompletionObservers ?? [],
    ];
    private Task? _initialization;
    private PluginAutomationExecutionService? _pluginExecution;

    internal void UsePluginExecution(PluginAutomationExecutionService execution) =>
        _pluginExecution = execution;

    public async Task<AutomationDispatchOutcome> DispatchAsync(
        AutomationTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        var dispatch = await DispatchCoreAsync(trigger, null, null, cancellationToken);
        return dispatch switch
        {
            CustomCommandAutomationAdmissionOutcome.Dispatched dispatched => dispatched.Dispatch,
            CustomCommandAutomationAdmissionOutcome.AlreadyUsed =>
                throw new InvalidOperationException(
                    "A generic automation dispatch cannot reject a custom-command invocation claim."
                ),
            _ => throw new InvalidOperationException("Unknown automation admission outcome."),
        };
    }

    internal Task<CustomCommandAutomationAdmissionOutcome> DispatchCustomCommandAsync(
        AutomationTrigger trigger,
        Func<
            BlokeBotDbContext,
            CancellationToken,
            Task<CustomCommandInvocationClaimOutcome>
        >? claim,
        Action onCommitted,
        CancellationToken cancellationToken
    ) => DispatchCoreAsync(trigger, claim, onCommitted, cancellationToken);

    internal async Task<AutomationDispatchOutcome> DispatchTwitchEventAsync(
        AutomationContext context,
        Func<AutomationConfiguration, bool> matches,
        CancellationToken cancellationToken
    )
    {
        var dispatch = await DispatchCoreAsync(context, matches, null, null, cancellationToken);
        return dispatch switch
        {
            CustomCommandAutomationAdmissionOutcome.Dispatched dispatched => dispatched.Dispatch,
            _ => throw new InvalidOperationException(
                "A Twitch event dispatch cannot reject a custom-command invocation claim."
            ),
        };
    }

    internal async Task<AutomationDispatchOutcome> DispatchExpandedAsync(
        AutomationContext context,
        Func<AutomationConfiguration, bool> matches,
        CancellationToken cancellationToken,
        Func<BlokeBotDbContext, CancellationToken, Task<bool>>? currentOccurrence = null,
        AutomationFlowId? selectedFlow = null,
        bool deferExecution = false,
        AutomationNodeId? selectedSource = null
    )
    {
        var result = await DispatchCoreAsync(
            context,
            matches,
            null,
            null,
            cancellationToken,
            async (db, ct) =>
                await AcceptExpandedOccurrenceAsync(db, context, ct)
                && (currentOccurrence is null || await currentOccurrence(db, ct)),
            selectedFlow,
            !deferExecution,
            selectedSource
        );
        return ((CustomCommandAutomationAdmissionOutcome.Dispatched)result).Dispatch;
    }

    private Task<CustomCommandAutomationAdmissionOutcome> DispatchCoreAsync(
        AutomationTrigger trigger,
        Func<
            BlokeBotDbContext,
            CancellationToken,
            Task<CustomCommandInvocationClaimOutcome>
        >? claim,
        Action? onCommitted,
        CancellationToken cancellationToken
    ) =>
        DispatchCoreAsync(
            trigger.Context,
            configuration => Equals(configuration, trigger.SourceConfiguration),
            claim,
            onCommitted,
            cancellationToken
        );

    private async Task<CustomCommandAutomationAdmissionOutcome> DispatchCoreAsync(
        AutomationContext context,
        Func<AutomationConfiguration, bool> matches,
        Func<
            BlokeBotDbContext,
            CancellationToken,
            Task<CustomCommandInvocationClaimOutcome>
        >? claim,
        Action? onCommitted,
        CancellationToken cancellationToken,
        Func<BlokeBotDbContext, CancellationToken, Task<bool>>? currentOccurrence = null,
        AutomationFlowId? selectedFlow = null,
        bool resumeImmediately = true,
        AutomationNodeId? selectedSource = null
    )
    {
        await EnsureInitializedAsync(cancellationToken);
        AutomationDispatchOutcome dispatch;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            if (
                await MainDatabaseStatements.LockHostAsync(
                    db,
                    context.HostId.Value,
                    cancellationToken
                ) == 0
            )
            {
                return new CustomCommandAutomationAdmissionOutcome.Dispatched(
                    new(AutomationDispatchStatus.HostNotFound, [])
                );
            }
            if (
                !await AutomationCommandSource.PreparedIsCurrentAsync(
                    db,
                    context,
                    cancellationToken
                )
            )
            {
                return new CustomCommandAutomationAdmissionOutcome.Dispatched(
                    new(AutomationDispatchStatus.InvalidFlow, [])
                );
            }
            dispatch = await AdmitAsync(
                db,
                context,
                matches,
                cancellationToken,
                currentOccurrence,
                selectedFlow,
                selectedSource
            );
            if (dispatch.Status != AutomationDispatchStatus.Accepted)
            {
                return new CustomCommandAutomationAdmissionOutcome.Dispatched(dispatch);
            }
            if (
                claim is not null
                && await claim(db, cancellationToken)
                    is CustomCommandInvocationClaimOutcome.AlreadyUsed
            )
            {
                return new CustomCommandAutomationAdmissionOutcome.AlreadyUsed();
            }
            await transaction.CommitAsync(cancellationToken);
        }
        onCommitted?.Invoke();
        await RunAdmittedAsync(
            context.HostId,
            dispatch.RunIds,
            resumeImmediately,
            cancellationToken
        );
        return new CustomCommandAutomationAdmissionOutcome.Dispatched(dispatch);
    }

    internal async Task<IReadOnlySet<int>> AvailableCustomCommandIdsAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var gate = await AdmissionGateAsync(hostId, cancellationToken);
        if (
            gate is not AutomationRuntimeGate.Enabled enabled
            || !enabled.Features.Contains(HostFeatureFlags.CustomCommands)
        )
        {
            return new HashSet<int>();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var flows = await db
            .AutomationFlows.AsNoTracking()
            .Include(static flow => flow.Nodes)
            .Include(static flow => flow.Edges)
            .Where(flow =>
                flow.HostId == hostId.Value
                && flow.IsEnabled
                && flow.SchemaVersion == AutomationFlowSchema.CurrentVersion
            )
            .ToArrayAsync(cancellationToken);
        var commandIds = new HashSet<int>();
        foreach (var flow in flows)
        {
            if (!await ValidFlowAsync(flow, hostId, cancellationToken))
            {
                continue;
            }

            foreach (
                var source in flow.Nodes.Where(node =>
                    node.DefinitionId == AutomationDefinitionIds.CustomCommandSource.Value
                    && IsSourceDefinition(node)
                )
            )
            {
                var check = await catalog.ValidatePersistedForSaveAsync(
                    hostId,
                    Definition(source),
                    cancellationToken
                );
                if (
                    check is AutomationConfigurationCheck.Valid
                    {
                        Configuration: CustomCommandSourceConfiguration configuration,
                    }
                )
                {
                    _ = commandIds.Add(configuration.CommandId.Value);
                }
            }
        }

        return commandIds;
    }

    internal async Task<IReadOnlySet<string>> EnabledSourceDefinitionIdsAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    ) =>
        (await ConfiguredSourcesAsync(hostId, cancellationToken))
            .Where(static source => source.Eligibility is AutomationSourceEligibility.Enabled)
            .Select(static source => source.DefinitionId.Value)
            .ToHashSet(StringComparer.Ordinal);

    internal async Task<ImmutableArray<AutomationConfiguredSource>> ConfiguredSourcesAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var gate = await AdmissionGateAsync(hostId, cancellationToken);
        if (gate is not AutomationRuntimeGate.Enabled)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var flows = await db
            .AutomationFlows.AsNoTracking()
            .Include(static flow => flow.Nodes)
            .Include(static flow => flow.Edges)
            .Where(flow => flow.HostId == hostId.Value)
            .OrderBy(static flow => flow.Name)
            .ThenBy(static flow => flow.Id)
            .ToArrayAsync(cancellationToken);
        var enabledFeatures = await db
            .Hosts.Where(host => host.Id == hostId.Value)
            .Select(static host => host.EnabledFeatures)
            .SingleAsync(cancellationToken);
        var sources = ImmutableArray.CreateBuilder<AutomationConfiguredSource>();
        foreach (var flow in flows)
        {
            var eligibility = await SourceEligibilityAsync(
                flow,
                hostId,
                enabledFeatures,
                db,
                cancellationToken
            );
            var restoredFlow = AutomationFlowService.RestoreDraft(flow);
            foreach (var node in flow.Nodes.Where(IsSourceDefinition))
            {
                var configuration =
                    restoredFlow is AutomationFlowDraftRestoreOutcome.Available restored
                    && catalog.ValidatePersistedDefinition(
                        restored
                            .Draft.Nodes.Single(candidate => candidate.Id.Value == node.Id)
                            .Definition
                    )
                        is AutomationConfigurationCheck.Valid valid
                        ? valid.Configuration
                        : null;
                sources.Add(
                    new(
                        new(flow.Id),
                        flow.Name,
                        new(node.Id),
                        new(node.DefinitionId),
                        configuration,
                        eligibility
                    )
                );
            }
        }
        return sources.ToImmutable();
    }

    public async Task<AutomationResumeOutcome> ResumeAsync(
        AutomationRunId runId,
        CancellationToken cancellationToken
    )
    {
        await EnsureInitializedAsync(cancellationToken);
        var leaseId = Guid.NewGuid();
        AutomationResumeOutcome outcome;
        try
        {
            var claim = await ClaimRunAsync(runId, leaseId, cancellationToken);
            outcome = await claim.Match(
                owned => ResumeOwnedAsync(runId, owned.LeaseId, cancellationToken),
                unavailable => Task.FromResult(new AutomationResumeOutcome(unavailable.Status))
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecoverCancelledRunAsync(runId, leaseId);
            throw;
        }
        finally
        {
            await ReleaseRunAsync(runId, leaseId, CancellationToken.None);
        }

        if (outcome.Status is AutomationResumeStatus.Completed or AutomationResumeStatus.Failed)
        {
            foreach (var observer in _runCompletionObservers)
            {
                await observer.RunFinishedAsync(runId, outcome.Status, cancellationToken);
            }
        }

        return outcome;
    }

    internal Task InitializeAsync(CancellationToken cancellationToken) =>
        EnsureInitializedAsync(cancellationToken);

    internal async Task ResumeDueAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db
            .AutomationNodeRuns.AsNoTracking()
            .Where(value =>
                (value.Status == AutomationNodeRunStatus.Pending && value.AvailableAtUtc <= now)
                || (
                    value.Status == AutomationNodeRunStatus.Waiting
                    && value.OutcomeCode == "subflow-draining"
                    && value.Run.NodeRuns.All(node =>
                        node.Status != AutomationNodeRunStatus.Pending
                        && node.Status != AutomationNodeRunStatus.Running
                    )
                )
            )
            .OrderBy(static value => value.AvailableAtUtc)
            .Select(static value => value.RunId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        foreach (var id in due)
        {
            _ = await ResumeAsync(new(id), cancellationToken);
        }
    }

    private async Task<AutomationResumeOutcome> ResumeOwnedAsync(
        AutomationRunId runId,
        Guid leaseId,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var run = await db
                .AutomationFlowRuns.Include(static value => value.NodeRuns)
                .SingleOrDefaultAsync(value => value.Id == runId.Value, cancellationToken);
            if (run is null)
            {
                return new(AutomationResumeStatus.NotFound);
            }

            if (run.ExecutionLeaseId != leaseId)
            {
                return new(AutomationResumeStatus.Waiting);
            }

            if (Terminal(run.Status) is { } terminal)
            {
                return new(terminal);
            }

            var executionGate = await EnforceExecutionGateAsync(
                db,
                run,
                leaseId,
                cancellationToken
            );
            if (executionGate == AutomationExecutionGate.OwnershipLost)
            {
                continue;
            }
            if (executionGate != AutomationExecutionGate.Open)
            {
                return ExecutionBlocked(executionGate);
            }

            if (await QueueDrainedExitsAsync(db, run, leaseId, cancellationToken))
            {
                continue;
            }
            var now = clock.GetUtcNow().UtcDateTime;
            var pending = run
                .NodeRuns.Where(static value => value.Status == AutomationNodeRunStatus.Pending)
                .OrderBy(value => value.AvailableAtUtc > now)
                .ThenBy(static value => value.Sequence)
                .FirstOrDefault();
            if (pending is null)
            {
                if (
                    AutomationRuntimeSerialization.RestoreDefinition(run.DefinitionJson)
                        is AutomationDefinitionRestoreOutcome.Available waitingFlow
                    && !waitingFlow.Flow.Invocations.IsDefault
                    && waitingFlow
                        .Flow.Invocations.Reverse()
                        .Select(invocation =>
                            run.NodeRuns.FirstOrDefault(node =>
                                node.NodeId == invocation.CallerId
                                && node.Status == AutomationNodeRunStatus.Waiting
                            )
                        )
                        .FirstOrDefault(node => node is not null)
                        is { } unfinished
                )
                {
                    _ = await CompleteFailureAsync(
                        new(
                            db,
                            run,
                            unfinished,
                            waitingFlow.Flow.Nodes.Single(node => node.Id == unfinished.NodeId),
                            waitingFlow.Flow,
                            leaseId
                        ),
                        "subflow-exit-not-reached",
                        cancellationToken
                    );
                    continue;
                }
                if (
                    run.NodeRuns.Any(node =>
                        node.Status
                            is AutomationNodeRunStatus.Running
                                or AutomationNodeRunStatus.Waiting
                    )
                )
                {
                    return new(AutomationResumeStatus.Waiting);
                }
                await using var completionTransaction = await db.Database.BeginTransactionAsync(
                    cancellationToken
                );
                var completed = await OwnedActiveRuns(db, run.Id, leaseId)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(
                                    static value => value.Status,
                                    AutomationFlowRunStatus.Completed
                                )
                                .SetProperty(static value => value.CompletedAtUtc, now),
                        cancellationToken
                    );
                if (completed == 0)
                {
                    continue;
                }

                await CompleteTerminalAsync(
                    db,
                    run,
                    AutomationTraceOutcome.Succeeded,
                    cancellationToken
                );
                _ = await db.SaveChangesAsync(cancellationToken);
                await completionTransaction.CommitAsync(cancellationToken);
                await uiChanges.RunChangedAsync(new(run.HostId), new(run.Id));
                return new(AutomationResumeStatus.Completed);
            }

            if (pending.AvailableAtUtc > now)
            {
                var waiting = await OwnedActiveRuns(db, run.Id, leaseId)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters.SetProperty(
                                static value => value.Status,
                                AutomationFlowRunStatus.Waiting
                            ),
                        cancellationToken
                    );
                if (waiting == 0)
                {
                    continue;
                }

                if (run.Status != AutomationFlowRunStatus.Waiting)
                {
                    await uiChanges.RunChangedAsync(new(run.HostId), new(run.Id));
                }
                return new(AutomationResumeStatus.Waiting);
            }

            if (!await ClaimNodeAsync(db, run, pending, leaseId, now, cancellationToken))
            {
                continue;
            }

            db.ChangeTracker.Clear();
            run = await db
                .AutomationFlowRuns.Include(static value => value.NodeRuns)
                .SingleAsync(value => value.Id == runId.Value, cancellationToken);
            pending = run.NodeRuns.Single(value => value.Id == pending.Id);

            executionGate = await EnforceExecutionGateAsync(db, run, leaseId, cancellationToken);
            if (executionGate == AutomationExecutionGate.OwnershipLost)
            {
                continue;
            }
            if (executionGate != AutomationExecutionGate.Open)
            {
                return ExecutionBlocked(executionGate);
            }

            if (
                AutomationRuntimeSerialization.RestoreDefinition(run.DefinitionJson)
                is not AutomationDefinitionRestoreOutcome.Available restoredDefinition
            )
            {
                await using var malformedTransaction = await db.Database.BeginTransactionAsync(
                    cancellationToken
                );
                if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
                {
                    continue;
                }
                FailMalformedRun(run, now);
                await CompleteTerminalAsync(
                    db,
                    run,
                    AutomationTraceOutcome.Failed,
                    cancellationToken
                );
                _ = await db.SaveChangesAsync(cancellationToken);
                await malformedTransaction.CommitAsync(cancellationToken);
                await uiChanges.RunChangedAsync(new(run.HostId), new(run.Id));
                return new(AutomationResumeStatus.Failed);
            }

            var flow = restoredDefinition.Flow;
            var validation = flow.Invocations.IsDefault
                ? await flowService.ValidateFrozenDefinitionAsync(
                    new(run.HostId),
                    flow,
                    cancellationToken
                )
                : new AutomationGraphValidation(null, []);
            var node = flow.Nodes.SingleOrDefault(value => value.Id == pending.NodeId);
            if (validation.Gate is not null || !validation.Errors.IsEmpty || node is null)
            {
                await using var malformedTransaction = await db.Database.BeginTransactionAsync(
                    cancellationToken
                );
                if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
                {
                    continue;
                }
                FailMalformedRun(run, now);
                await CompleteTerminalAsync(
                    db,
                    run,
                    AutomationTraceOutcome.Failed,
                    cancellationToken
                );
                _ = await db.SaveChangesAsync(cancellationToken);
                await malformedTransaction.CommitAsync(cancellationToken);
                await uiChanges.RunChangedAsync(new(run.HostId), new(run.Id));
                return new(AutomationResumeStatus.Failed);
            }

            var scope = new AutomationNodeExecutionScope(db, run, pending, node, flow, leaseId);
            if (
                node.DefinitionId == AutomationSubflowDefinitions.Exit
                && AutomationFrozenSubflows.HasActiveDescendants(
                    flow,
                    flow.Invocations.Single(invocation => invocation.ExitId == node.Id),
                    ActiveNodes(run)
                )
            )
            {
                await WaitForSubtreeAsync(scope, cancellationToken);
                continue;
            }
            if (node.ExpressionLanguageVersion != AutomationExpressionLanguage.CurrentVersion.Value)
            {
                if (
                    await StopsFlowAsync(scope, "expression-version-unsupported", cancellationToken)
                )
                {
                    return new(AutomationResumeStatus.Failed);
                }

                continue;
            }

            var restored = AutomationRuntimeSerialization.RestoreContext(
                run.ContextSchemaVersion,
                run.ContextJson
            );
            if (restored is not AutomationContextRestoreOutcome.Available available)
            {
                if (await StopsFlowAsync(scope, "context-version-unsupported", cancellationToken))
                {
                    return new(AutomationResumeStatus.Failed);
                }

                continue;
            }

            var check = AutomationFrozenSubflows.WithContract(
                node,
                await AutomationFrozenSubflows.ValidateBeforeExecutionAsync(
                    catalog,
                    new(run.HostId),
                    available.Context,
                    node,
                    cancellationToken
                )
            );
            if (check is not AutomationConfigurationCheck.Valid valid)
            {
                if (await StopsFlowAsync(scope, "configuration-invalid", cancellationToken))
                {
                    return new(AutomationResumeStatus.Failed);
                }

                continue;
            }

            var inputs = await catalog.Data.ResolveInputsAsync(
                new(run.HostId),
                available.Context,
                flow,
                node,
                new RuntimeCheckpointStore(db, run, leaseId, clock),
                cancellationToken
            );
            var resumed = await inputs.Match<Task<AutomationResumeOutcome?>>(
                async resolvedInputs =>
                {
                    await TraceAsync(
                        db,
                        run,
                        AutomationTraceEventKind.ResolvedInputs,
                        cancellationToken,
                        node,
                        values: resolvedInputs.PortValues
                    );
                    await TraceAsync(
                        db,
                        run,
                        AutomationTraceEventKind.Attempt,
                        cancellationToken,
                        node
                    );
                    if (valid.Configuration is AutomationSubflowConfiguration)
                    {
                        _ = await CompleteBoundaryAsync(scope, resolvedInputs, cancellationToken);
                        return null;
                    }
                    var result = await ExecuteNodeAsync(
                        new(run.HostId),
                        valid.Configuration,
                        node,
                        available.Context,
                        resolvedInputs.FieldValues,
                        cancellationToken
                    );
                    executionGate = await EnforceExecutionGateAsync(
                        db,
                        run,
                        leaseId,
                        cancellationToken
                    );
                    if (executionGate == AutomationExecutionGate.OwnershipLost)
                    {
                        return null;
                    }
                    if (executionGate != AutomationExecutionGate.Open)
                    {
                        return ExecutionBlocked(executionGate);
                    }

                    var stopped = await result.Match(
                        async succeeded =>
                        {
                            _ = await CompleteSuccessAsync(scope, succeeded, cancellationToken);
                            return false;
                        },
                        failure => StopsFlowAsync(scope, failure.Code, cancellationToken)
                    );
                    return stopped ? new(AutomationResumeStatus.Failed) : null;
                },
                async failure =>
                {
                    executionGate = await EnforceExecutionGateAsync(
                        db,
                        run,
                        leaseId,
                        cancellationToken
                    );
                    return executionGate switch
                    {
                        AutomationExecutionGate.OwnershipLost => null,
                        AutomationExecutionGate.HostNotFound
                        or AutomationExecutionGate.Invalidated => ExecutionBlocked(executionGate),
                        AutomationExecutionGate.Open => await StopsFlowAsync(
                            scope,
                            "input-resolution-failed",
                            cancellationToken,
                            failure.Diagnostic
                        )
                            ? new(AutomationResumeStatus.Failed)
                            : null,
                    };
                }
            );
            if (resumed is not null)
            {
                return resumed;
            }
        }
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        lock (_initializationGate)
        {
            return _initialization ??= RecoverInterruptedAsync(cancellationToken);
        }
    }

    private static void FinishInterruptedNodes(
        IEnumerable<AutomationNodeRun> interrupted,
        AutomationFlowRunStatus terminalStatus,
        DateTime now
    )
    {
        foreach (var node in interrupted)
        {
            node.Status =
                terminalStatus == AutomationFlowRunStatus.Invalidated
                    ? AutomationNodeRunStatus.Invalidated
                    : AutomationNodeRunStatus.Failed;
            node.OutcomeCode =
                terminalStatus == AutomationFlowRunStatus.Invalidated
                    ? "run-invalidated"
                    : "execution-interrupted";
            node.CompletedAtUtc = now;
        }
    }

    private static void FailMalformedRun(AutomationFlowRun run, DateTime now)
    {
        run.Status = AutomationFlowRunStatus.Failed;
        run.CompletedAtUtc = now;
        foreach (
            var node in run.NodeRuns.Where(static node =>
                node.Status
                    is AutomationNodeRunStatus.Pending
                        or AutomationNodeRunStatus.Running
                        or AutomationNodeRunStatus.Waiting
            )
        )
        {
            node.Status = AutomationNodeRunStatus.Failed;
            node.OutcomeCode = "definition-invalid";
            node.CompletedAtUtc = now;
        }
    }

    private static void FailInterruptedRun(
        AutomationFlowRun run,
        IEnumerable<AutomationNodeRun> interrupted,
        IReadOnlyDictionary<Guid, AutomationRuntimeSerialization.PersistedNode> definitions,
        DateTime now
    )
    {
        run.Status = AutomationFlowRunStatus.Failed;
        run.CompletedAtUtc = now;
        foreach (var node in interrupted)
        {
            node.Status = AutomationNodeRunStatus.Failed;
            node.OutcomeCode = definitions[node.NodeId].ContinueOnFailure
                ? "flow-stopped"
                : "execution-interrupted";
            node.CompletedAtUtc = now;
        }

        foreach (
            var pending in run.NodeRuns.Where(static value =>
                value.Status is AutomationNodeRunStatus.Pending or AutomationNodeRunStatus.Waiting
            )
        )
        {
            pending.Status = AutomationNodeRunStatus.Failed;
            pending.OutcomeCode = "flow-stopped";
            pending.CompletedAtUtc = now;
        }
    }

    private static void ContinueInterruptedRun(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        IEnumerable<AutomationNodeRun> interrupted,
        IReadOnlyDictionary<Guid, AutomationRuntimeSerialization.PersistedNode> definitions,
        AutomationRuntimeSerialization.PersistedFlow flow,
        DateTime now
    )
    {
        var existingNodes = run.NodeRuns.Select(static node => node.NodeId).ToHashSet();
        var sequence = run.NodeRuns.Max(static value => value.Sequence) + 1;
        var scheduled = 0;
        foreach (var node in interrupted)
        {
            node.Status = AutomationNodeRunStatus.ContinuedAfterFailure;
            node.OutcomeCode = "execution-interrupted";
            node.CompletedAtUtc = now;
            foreach (
                var next in AutomationFlowTraversal.Schedule(
                    flow.Edges,
                    definitions[node.NodeId].Id,
                    new AutomationOutputSelection.Selected([new("complete")]),
                    existingNodes
                )
            )
            {
                AddPending(db, run.Id, [next], now, sequence++);
                scheduled++;
            }
        }

        var hasPending = run.NodeRuns.Any(static node =>
            node.Status == AutomationNodeRunStatus.Pending
        );
        run.Status =
            hasPending || scheduled > 0
                ? AutomationFlowRunStatus.Running
                : AutomationFlowRunStatus.Completed;
        run.CompletedAtUtc = run.Status == AutomationFlowRunStatus.Completed ? now : null;
    }

    private async Task<AutomationRunClaim> ClaimRunAsync(
        AutomationRunId runId,
        Guid leaseId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await db
            .AutomationFlowRuns.Where(value =>
                value.Id == runId.Value
                && value.ExecutionLeaseId == null
                && (
                    value.Status == AutomationFlowRunStatus.Running
                    || value.Status == AutomationFlowRunStatus.Waiting
                )
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(static value => value.ExecutionLeaseId, leaseId),
                cancellationToken
            );
        if (claimed == 1)
        {
            var run = await db.AutomationFlowRuns.SingleAsync(
                value => value.Id == runId.Value,
                cancellationToken
            );
            await TraceAsync(db, run, AutomationTraceEventKind.Resume, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AutomationRunClaim.Owned(leaseId);
        }

        var state = await db
            .AutomationFlowRuns.AsNoTracking()
            .Where(value => value.Id == runId.Value)
            .Select(static value => new { value.Status, value.ExecutionLeaseId })
            .SingleOrDefaultAsync(cancellationToken);
        return state switch
        {
            null => new AutomationRunClaim.Unavailable(AutomationResumeStatus.NotFound),
            { Status: var status } when Terminal(status) is { } terminal =>
                new AutomationRunClaim.Unavailable(terminal),
            _ => new AutomationRunClaim.Unavailable(AutomationResumeStatus.Waiting),
        };
    }

    private async Task ReleaseRunAsync(
        AutomationRunId runId,
        Guid leaseId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        _ = await db
            .AutomationFlowRuns.Where(value =>
                value.Id == runId.Value && value.ExecutionLeaseId == leaseId
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(static value => value.ExecutionLeaseId, static _ => null),
                cancellationToken
            );
    }

    private static async Task<bool> ClaimNodeAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        AutomationNodeRun node,
        Guid leaseId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var claimed = await db
            .AutomationNodeRuns.Where(value =>
                value.Id == node.Id && value.Status == AutomationNodeRunStatus.Pending
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(static value => value.Status, AutomationNodeRunStatus.Running)
                        .SetProperty(static value => value.StartedAtUtc, now),
                cancellationToken
            );
        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> CompleteSuccessAsync(
        AutomationNodeExecutionScope scope,
        AutomationNodeExecution.Succeeded succeeded,
        CancellationToken cancellationToken
    )
    {
        var (db, run, nodeRun, node, flow, leaseId) = scope;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        nodeRun.Status = AutomationNodeRunStatus.Succeeded;
        nodeRun.OutcomeCode = succeeded.Code;
        nodeRun.CompletedAtUtc = now;
        await TraceAsync(
            db,
            run,
            AutomationTraceEventKind.Result,
            cancellationToken,
            node,
            AutomationTraceOutcome.Succeeded
        );
        foreach (var port in succeeded.OutputSelection.BranchPorts)
        {
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.Branch,
                cancellationToken,
                node,
                port: port?.Value
            );
        }
        if (
            node.DefinitionId == AutomationDefinitionIds.DelayControl.Value
            || succeeded.NextAvailableAtUtc > now
        )
        {
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.Delay,
                cancellationToken,
                node,
                due: succeeded.NextAvailableAtUtc
            );
        }
        var next = AutomationFlowTraversal.Schedule(
            flow.Edges,
            node.Id,
            succeeded.OutputSelection,
            run.NodeRuns.Select(static value => value.NodeId).ToHashSet()
        );
        AddPending(
            db,
            run.Id,
            next,
            succeeded.NextAvailableAtUtc,
            run.NodeRuns.Max(static value => value.Sequence) + 1
        );
        await TraceScheduledAsync(
            db,
            run,
            flow,
            next,
            succeeded.NextAvailableAtUtc,
            cancellationToken
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> StopsFlowAsync(
        AutomationNodeExecutionScope scope,
        string code,
        CancellationToken cancellationToken,
        AutomationDiagnostic? diagnostic = null
    ) =>
        await CompleteFailureAsync(scope, code, cancellationToken, diagnostic)
        == AutomationFailureCompletion.Stopped;

    private async Task<AutomationFailureCompletion> CompleteFailureAsync(
        AutomationNodeExecutionScope scope,
        string code,
        CancellationToken cancellationToken,
        AutomationDiagnostic? diagnostic = null
    )
    {
        var (db, run, nodeRun, node, flow, leaseId) = scope;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AutomationFailureCompletion.LostOwnership;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        nodeRun.OutcomeCode = code;
        nodeRun.CompletedAtUtc = now;
        await TraceAsync(
            db,
            run,
            AutomationTraceEventKind.Result,
            cancellationToken,
            node,
            node.ContinueOnFailure
                ? AutomationTraceOutcome.ContinuedAfterFailure
                : AutomationTraceOutcome.Failed,
            diagnostic: diagnostic
        );
        if (
            nodeRun.Status == AutomationNodeRunStatus.Waiting
            && AutomationFrozenSubflows.Invocation(flow, node.Id) is { } ownInvocation
        )
        {
            await AutomationSubflowExecutionTrace.CloseOpenAsync(
                db,
                run,
                AutomationTraceOutcome.Failed,
                now,
                cancellationToken,
                ownInvocation.Path
            );
        }
        nodeRun.Status = node.ContinueOnFailure
            ? AutomationNodeRunStatus.ContinuedAfterFailure
            : AutomationNodeRunStatus.Failed;
        var failureNode = await UnwindFailureAsync(scope, cancellationToken, diagnostic);
        if (!failureNode.ContinueOnFailure)
        {
            run.Status = AutomationFlowRunStatus.Failed;
            run.CompletedAtUtc = now;
            foreach (
                var pending in run.NodeRuns.Where(static value =>
                    value.Status
                        is AutomationNodeRunStatus.Pending
                            or AutomationNodeRunStatus.Waiting
                )
            )
            {
                pending.Status = AutomationNodeRunStatus.Failed;
                pending.OutcomeCode = "flow-stopped";
                pending.CompletedAtUtc = now;
            }

            await CompleteTerminalAsync(db, run, AutomationTraceOutcome.Failed, cancellationToken);
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await uiChanges.RunChangedAsync(new(run.HostId), new(run.Id));
            return AutomationFailureCompletion.Stopped;
        }

        var next = AutomationFlowTraversal.Schedule(
            flow.Edges,
            AutomationFrozenSubflows.Continuation(flow, failureNode).Id,
            new AutomationOutputSelection.Selected([new("complete")]),
            run.NodeRuns.Select(static value => value.NodeId).ToHashSet()
        );
        AddPending(db, run.Id, next, now, run.NodeRuns.Max(static value => value.Sequence) + 1);
        await TraceScheduledAsync(db, run, flow, next, now, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AutomationFailureCompletion.Continued;
    }

    private async Task<bool> InvalidateOwnedAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        Guid leaseId,
        string outcomeCode,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        Invalidate(run, clock.GetUtcNow().UtcDateTime, outcomeCode);
        await TraceAsync(
            db,
            run,
            AutomationTraceEventKind.Cancellation,
            cancellationToken,
            outcome: AutomationTraceOutcome.Invalidated
        );
        await CompleteTerminalAsync(db, run, AutomationTraceOutcome.Invalidated, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await uiChanges.RunChangedAsync(new(run.HostId), new(run.Id));
        return true;
    }

    private static Task<int> TouchOwnedRunAsync(
        BlokeBotDbContext db,
        Guid runId,
        Guid leaseId,
        CancellationToken cancellationToken
    ) =>
        OwnedActiveRuns(db, runId, leaseId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(static value => value.ExecutionLeaseId, leaseId),
                cancellationToken
            );

    private static IQueryable<AutomationFlowRun> OwnedActiveRuns(
        BlokeBotDbContext db,
        Guid runId,
        Guid leaseId
    ) =>
        db.AutomationFlowRuns.Where(value =>
            value.Id == runId
            && value.ExecutionLeaseId == leaseId
            && (
                value.Status == AutomationFlowRunStatus.Running
                || value.Status == AutomationFlowRunStatus.Waiting
            )
        );

    private async Task<AutomationNodeExecution> ExecuteNodeAsync(
        AutomationHostId hostId,
        AutomationConfiguration configuration,
        AutomationRuntimeSerialization.PersistedNode node,
        AutomationContext context,
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        CancellationToken cancellationToken
    ) =>
        configuration switch
        {
            PluginAutomationConfiguration plugin when _pluginExecution is not null =>
                await ExecutePluginAsync(
                    hostId,
                    new(node.DefinitionId),
                    plugin,
                    context,
                    inputs,
                    cancellationToken
                ),
            EndAutomationControlConfiguration => AutomationNodeEvaluation.End(
                clock.GetUtcNow().UtcDateTime
            ),
            ConditionControlConfiguration => AutomationNodeEvaluation.Condition(
                inputs,
                clock.GetUtcNow().UtcDateTime
            ),
            DelayControlConfiguration delay => AutomationNodeEvaluation.Delay(
                delay,
                inputs,
                clock.GetUtcNow().UtcDateTime
            ),
            _ => await ExecuteActionAsync(
                hostId,
                configuration,
                node,
                context,
                inputs,
                cancellationToken
            ),
        };

    private async Task<AutomationNodeExecution> ExecutePluginAsync(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        PluginAutomationConfiguration configuration,
        AutomationContext context,
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        CancellationToken cancellationToken
    )
    {
        var outcome = await _pluginExecution!.ExecuteActionAsync(
            hostId,
            definitionId,
            configuration,
            inputs,
            context,
            cancellationToken
        );
        return AutomationNodeEvaluation.Action(
            outcome,
            clock.GetUtcNow().UtcDateTime,
            "plugin-succeeded"
        );
    }

    private async Task<AutomationNodeExecution> ExecuteActionAsync(
        AutomationHostId hostId,
        AutomationConfiguration configuration,
        AutomationRuntimeSerialization.PersistedNode node,
        AutomationContext context,
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs,
        CancellationToken cancellationToken
    )
    {
        if (
            AutomationRuntimeSerialization.RestoreInputBindings(
                node.InputBindingsJson,
                new(node.DefinitionId)
            )
            is not AutomationInputBindingsRestoreOutcome.Available restored
        )
        {
            return new AutomationNodeExecution.Failed("binding-invalid");
        }

        var activeExpressions = restored
            .Bindings.Where(static pair => pair.Value.Mode == AutomationInputBindingMode.Expression)
            .ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value.Expression!);
        var outcome = await actions.ExecuteAsync(
            hostId,
            configuration,
            activeExpressions,
            inputs,
            context,
            cancellationToken
        );
        return AutomationNodeEvaluation.Action(
            outcome,
            clock.GetUtcNow().UtcDateTime,
            "action-succeeded"
        );
    }

    private static void Invalidate(AutomationFlowRun run, DateTime now, string outcomeCode)
    {
        run.Status = AutomationFlowRunStatus.Invalidated;
        run.CompletedAtUtc = now;
        foreach (
            var node in run.NodeRuns.Where(static value =>
                value.Status
                    is AutomationNodeRunStatus.Pending
                        or AutomationNodeRunStatus.Running
                        or AutomationNodeRunStatus.Waiting
            )
        )
        {
            node.Status = AutomationNodeRunStatus.Invalidated;
            node.OutcomeCode = outcomeCode;
            node.CompletedAtUtc = now;
        }
    }

    private static void AddPending(
        BlokeBotDbContext db,
        Guid runId,
        ImmutableArray<Guid> nodes,
        DateTime availableAtUtc,
        long firstSequence
    )
    {
        var sequence = firstSequence;
        foreach (var nodeId in nodes)
        {
            _ = db.AutomationNodeRuns.Add(
                new()
                {
                    RunId = runId,
                    NodeId = nodeId,
                    Sequence = sequence++,
                    Status = AutomationNodeRunStatus.Pending,
                    AvailableAtUtc = availableAtUtc,
                }
            );
        }
    }

    private bool IsSourceDefinition(AutomationFlowNode node) =>
        catalog.TryDescribe(new(node.DefinitionId), out var definition)
        && definition.Kind == AutomationNodeKind.Source;

    private async Task<bool> ValidFlowAsync(
        AutomationFlow flow,
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        if (
            AutomationFlowService.RestoreDraft(flow)
                is not AutomationFlowDraftRestoreOutcome.Available restored
            || restored.Draft.HostId != hostId
        )
        {
            return false;
        }
        var validation = await flowService.ValidateAsync(restored.Draft, cancellationToken);
        return validation.Gate is null
            && validation.Errors.All(static error => error.Code == "capability-unavailable");
    }

    private async Task<AutomationSourceEligibility> SourceEligibilityAsync(
        AutomationFlow flow,
        AutomationHostId hostId,
        HostFeatureFlags enabledFeatures,
        BlokeBotDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (!flow.IsEnabled)
        {
            return new AutomationSourceEligibility.Disabled();
        }
        if (flow.SchemaVersion != AutomationFlowSchema.CurrentVersion)
        {
            return new AutomationSourceEligibility.Unavailable(
                "This saved flow needs a supported current schema before its sources can run."
            );
        }
        if (
            AutomationFlowService.RestoreDraft(flow)
                is not AutomationFlowDraftRestoreOutcome.Available restored
            || restored.Draft.HostId != hostId
        )
        {
            return new AutomationSourceEligibility.Unavailable(
                "The saved flow is invalid. Repair it before enabling its sources."
            );
        }
        var resolved = await AutomationSubflowStore.LoadClosureAsync(
            db,
            hostId,
            AutomationSubflowStore
                .Calls(restored.Draft.Nodes)
                .Select(static call => call.SubflowId),
            null,
            cancellationToken
        );
        if (resolved is not AutomationSubflowClosureOutcome.Available current)
        {
            return new AutomationSourceEligibility.Unavailable(
                "A called subflow is unavailable. Repair the flow before using its sources."
            );
        }
        var validation = await flowService.ValidatePreparedAsync(
            restored.Draft,
            current.Closure,
            AutomationFlowService.AutomationGraphAdmission.Saved,
            cancellationToken,
            db
        );
        if (
            validation.Gate is not null
            || validation.Errors.Any(static error => error.Code != "capability-unavailable")
        )
        {
            return new AutomationSourceEligibility.Unavailable(
                "The saved flow is invalid. Repair it before using its sources."
            );
        }
        var original = (
            (AutomationDefinitionRestoreOutcome.Available)
                AutomationRuntimeSerialization.RestoreDefinition(
                    AutomationRuntimeSerialization.SerializeDefinition(flow)
                )
        ).Flow;
        var frozen = AutomationFrozenSubflows.Freeze(original, Guid.NewGuid(), current.Closure);
        if (frozen is null || FreezeExecutionFences(frozen.Flow) is not { } frozenFlow)
        {
            return new AutomationSourceEligibility.Unavailable(
                "The flow's execution definitions are unavailable. Repair the flow before using its sources."
            );
        }
        var required = AutomationRequiredFeatures.ForDefinitions(
            frozenFlow.Nodes.Select(static node => node.DefinitionId)
        );
        return enabledFeatures.Contains(required)
            ? new AutomationSourceEligibility.Enabled()
            : new AutomationSourceEligibility.Unavailable(
                $"Enable the flow's required channel features: {AutomationRequiredFeatures.BackingFeatures(required & ~enabledFeatures)}."
            );
    }

    private static PersistedAutomationNodeDefinition Definition(AutomationFlowNode node) =>
        new(
            node.DefinitionId,
            node.DefinitionSchemaVersion,
            System.Text.Json.JsonDocument.Parse(node.ConfigurationJson).RootElement.Clone(),
            PluginAutomationCatalogRegistry.TryDeserializeProvenance(
                node.PluginProvenanceJson,
                out var provenance
            )
                ? provenance
                : null
        );

    private AutomationPluginProvenance? CurrentPluginProvenance(int hostId, string definitionId) =>
        catalog.TryResolvePlugin(new(hostId), new(definitionId), out var definition)
            ? definition.Descriptor.PluginProvenance
            : null;

    private static AutomationResumeStatus? Terminal(AutomationFlowRunStatus status) =>
        status switch
        {
            AutomationFlowRunStatus.Completed => AutomationResumeStatus.Completed,
            AutomationFlowRunStatus.Failed => AutomationResumeStatus.Failed,
            AutomationFlowRunStatus.Invalidated => AutomationResumeStatus.Invalidated,
            _ => null,
        };

    private abstract record AutomationRunClaim
    {
        private AutomationRunClaim() { }

        internal abstract TResult Match<TResult>(
            Func<Owned, TResult> owned,
            Func<Unavailable, TResult> unavailable
        );

        internal sealed record Owned(Guid LeaseId) : AutomationRunClaim
        {
            internal override TResult Match<TResult>(
                Func<Owned, TResult> owned,
                Func<Unavailable, TResult> unavailable
            ) => owned(this);
        }

        internal sealed record Unavailable(AutomationResumeStatus Status) : AutomationRunClaim
        {
            internal override TResult Match<TResult>(
                Func<Owned, TResult> owned,
                Func<Unavailable, TResult> unavailable
            ) => unavailable(this);
        }
    }

    private sealed class RuntimeCheckpointStore(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        Guid leaseId,
        TimeProvider clock
    ) : IAutomationPureCheckpointStore
    {
        public async ValueTask<AutomationPureCheckpoint> ReadOrBeginAsync(
            AutomationRuntimeSerialization.PersistedNode node,
            CancellationToken cancellationToken
        )
        {
            var existing = run.NodeRuns.SingleOrDefault(value => value.NodeId == node.Id);
            if (
                existing
                    is { Status: AutomationNodeRunStatus.Succeeded, OutputJson: { } outputJson }
                && AutomationDataValueSerialization.RestoreOutputs(outputJson)
                    is AutomationOutputRestoreOutcome.Available restored
            )
            {
                return new AutomationPureCheckpoint.Available(restored.Outputs);
            }

            if (
                node.DefinitionId
                is AutomationSubflowDefinitions.Entry
                    or AutomationSubflowDefinitions.Invoke
            )
            {
                return new AutomationPureCheckpoint.Failed();
            }
            if (existing is not null)
            {
                if (existing.Status == AutomationNodeRunStatus.Succeeded)
                {
                    existing.Status = AutomationNodeRunStatus.Failed;
                    existing.OutcomeCode = "output-invalid";
                    existing.OutputJson = null;
                    existing.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
                    _ = await db.SaveChangesAsync(cancellationToken);
                }

                return new AutomationPureCheckpoint.Failed();
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var checkpoint = new AutomationNodeRun
            {
                RunId = run.Id,
                NodeId = node.Id,
                Sequence = run.NodeRuns.Max(static value => value.Sequence) + 1,
                Status = AutomationNodeRunStatus.Running,
                AvailableAtUtc = now,
                StartedAtUtc = now,
            };
            run.NodeRuns.Add(checkpoint);
            _ = await db.SaveChangesAsync(cancellationToken);
            return new AutomationPureCheckpoint.Begin();
        }

        public async ValueTask RecordInputsAsync(
            AutomationRuntimeSerialization.PersistedNode node,
            ImmutableDictionary<AutomationPortId, AutomationResolvedValue> inputs,
            CancellationToken cancellationToken
        )
        {
            var now = clock.GetUtcNow().UtcDateTime;
            await AutomationTraceStore.AppendAsync(
                db,
                run.Id,
                AutomationTraceRedaction.Event(
                    run.Id,
                    AutomationTraceEventKind.ResolvedInputs,
                    now,
                    node,
                    values: inputs
                ),
                now,
                cancellationToken
            );
            await AutomationTraceStore.AppendAsync(
                db,
                run.Id,
                AutomationTraceRedaction.Event(run.Id, AutomationTraceEventKind.Attempt, now, node),
                now,
                cancellationToken
            );
        }

        public async ValueTask<bool> CompleteAsync(
            AutomationRuntimeSerialization.PersistedNode node,
            ImmutableDictionary<AutomationPortId, AutomationResolvedValue> outputs,
            CancellationToken cancellationToken
        )
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            var checkpoint = run.NodeRuns.Single(value => value.NodeId == node.Id);
            checkpoint.Status = AutomationNodeRunStatus.Succeeded;
            checkpoint.OutcomeCode = "output-checkpointed";
            checkpoint.OutputJson = AutomationDataValueSerialization.SerializeOutputs(outputs);
            checkpoint.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
            await AutomationTraceStore.AppendAsync(
                db,
                run.Id,
                AutomationTraceRedaction.Event(
                    run.Id,
                    AutomationTraceEventKind.Outputs,
                    checkpoint.CompletedAtUtc.Value,
                    node,
                    AutomationTraceOutcome.Succeeded,
                    values: outputs
                ),
                clock.GetUtcNow().UtcDateTime,
                cancellationToken
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        public async ValueTask FailAsync(
            AutomationRuntimeSerialization.PersistedNode node,
            string code,
            CancellationToken cancellationToken,
            AutomationDiagnostic? diagnostic = null
        )
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var checkpoint = run.NodeRuns.Single(value => value.NodeId == node.Id);
            checkpoint.Status = AutomationNodeRunStatus.Failed;
            checkpoint.OutcomeCode = code;
            checkpoint.OutputJson = null;
            checkpoint.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
            await AutomationTraceStore.AppendAsync(
                db,
                run.Id,
                AutomationTraceRedaction.Event(
                    run.Id,
                    AutomationTraceEventKind.Outputs,
                    clock.GetUtcNow().UtcDateTime,
                    node,
                    AutomationTraceOutcome.Failed,
                    diagnostic: diagnostic
                ),
                clock.GetUtcNow().UtcDateTime,
                cancellationToken
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private sealed record AutomationNodeExecutionScope(
        BlokeBotDbContext Db,
        AutomationFlowRun Run,
        AutomationNodeRun NodeRun,
        AutomationRuntimeSerialization.PersistedNode Node,
        AutomationRuntimeSerialization.PersistedFlow Flow,
        Guid LeaseId
    );

    private enum AutomationFailureCompletion
    {
        LostOwnership,
        Continued,
        Stopped,
    }
}

internal abstract record AutomationSourceEligibility
{
    private AutomationSourceEligibility() { }

    internal abstract TResult Match<TResult>(
        Func<Enabled, TResult> enabled,
        Func<Disabled, TResult> disabled,
        Func<Unavailable, TResult> unavailable
    );

    internal sealed record Enabled : AutomationSourceEligibility
    {
        internal override TResult Match<TResult>(
            Func<Enabled, TResult> enabled,
            Func<Disabled, TResult> disabled,
            Func<Unavailable, TResult> unavailable
        ) => enabled(this);
    }

    internal sealed record Disabled : AutomationSourceEligibility
    {
        internal override TResult Match<TResult>(
            Func<Enabled, TResult> enabled,
            Func<Disabled, TResult> disabled,
            Func<Unavailable, TResult> unavailable
        ) => disabled(this);
    }

    internal sealed record Unavailable(string Reason) : AutomationSourceEligibility
    {
        internal override TResult Match<TResult>(
            Func<Enabled, TResult> enabled,
            Func<Disabled, TResult> disabled,
            Func<Unavailable, TResult> unavailable
        ) => unavailable(this);
    }
}

internal sealed record AutomationConfiguredSource(
    AutomationFlowId FlowId,
    string FlowName,
    AutomationNodeId NodeId,
    AutomationDefinitionId DefinitionId,
    AutomationConfiguration? Configuration,
    AutomationSourceEligibility Eligibility
);
