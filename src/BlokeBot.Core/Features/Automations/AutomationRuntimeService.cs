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
    IEnumerable<IAutomationRunCompletionObserver>? runCompletionObservers = null
) : IPluginAutomationRunDispatcher
{
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
        CancellationToken cancellationToken
    )
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var flows = await db
            .AutomationFlows.AsNoTracking()
            .Include(static flow => flow.Nodes)
            .Include(static flow => flow.Edges)
            .Where(flow =>
                flow.HostId == context.HostId.Value
                && flow.IsEnabled
                && flow.SchemaVersion == AutomationFlowSchema.CurrentVersion
            )
            .OrderBy(static flow => flow.Id)
            .ToArrayAsync(cancellationToken);
        var matching = new List<(AutomationFlow Flow, AutomationFlowNode Source)>();
        var invalidFlow = false;
        foreach (var flow in flows)
        {
            var potentialSources = flow
                .Nodes.Where(node =>
                    node.DefinitionId == context.Event.SourceDefinitionId.Value
                    && IsSourceDefinition(node)
                )
                .ToArray();
            if (potentialSources.Length == 0)
            {
                continue;
            }

            if (
                AutomationFlowService.RestoreDraft(flow)
                is not AutomationFlowDraftRestoreOutcome.Available restored
            )
            {
                invalidFlow = true;
                continue;
            }

            var matchingSources = potentialSources
                .Where(source =>
                    catalog.ValidatePersistedDefinition(Definition(source))
                        is AutomationConfigurationCheck.Valid valid
                    && matches(valid.Configuration)
                )
                .ToArray();
            if (matchingSources.Length == 0)
            {
                continue;
            }

            var validation = await flowService.ValidateAsync(restored.Draft, cancellationToken);
            if (
                validation.Gate is not null
                || validation.Errors.Any(static error => error.Code != "capability-unavailable")
            )
            {
                invalidFlow = true;
                continue;
            }

            foreach (var source in matchingSources)
            {
                matching.Add((flow, source));
            }
        }

        var accepted = ImmutableArray.CreateBuilder<AutomationRunId>();
        var duplicateCount = 0;
        var blockedCount = 0;
        await using var dispatchTransaction = await db.Database.BeginTransactionAsync(
            cancellationToken
        );
        var hostExists = await MainDatabaseStatements.LockHostAsync(
            db,
            context.HostId.Value,
            cancellationToken
        );
        if (hostExists == 0)
        {
            return Dispatched(AutomationDispatchStatus.HostNotFound);
        }

        var host = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == context.HostId.Value)
            .Select(static value => new { value.EnabledFeatures, value.AutomationGeneration })
            .SingleAsync(cancellationToken);
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return Dispatched(AutomationDispatchStatus.FeatureDisabled);
        }

        if (invalidFlow)
        {
            return Dispatched(AutomationDispatchStatus.InvalidFlow);
        }

        if (matching.Count == 0)
        {
            return Dispatched(AutomationDispatchStatus.NoMatchingFlow);
        }

        foreach (var (flow, source) in matching)
        {
            var requiredFeatures = AutomationRequiredFeatures.ForDefinitions(
                flow.Nodes.Select(static node => node.DefinitionId)
            );
            if (!host.EnabledFeatures.Contains(requiredFeatures))
            {
                blockedCount++;
                continue;
            }

            var runId = Guid.NewGuid();
            var now = clock.GetUtcNow().UtcDateTime;
            var inserted = await MainDatabaseStatements.TryInsertAutomationFlowRunAsync(
                db,
                new(
                    runId,
                    flow.Id,
                    flow.HostId,
                    host.AutomationGeneration,
                    requiredFeatures,
                    AutomationContextSchema.CurrentVersion,
                    context.Event.SourceDefinitionId.Value,
                    source.Id,
                    context.Event.OccurrenceId,
                    AutomationRuntimeSerialization.SerializeContext(context),
                    AutomationRuntimeSerialization.SerializeDefinition(
                        flow,
                        definitionId => CurrentPluginProvenance(flow.HostId, definitionId)
                    ),
                    AutomationFlowRunStatus.Running,
                    now
                ),
                cancellationToken
            );
            if (inserted == 0)
            {
                var duplicate = await db.AutomationFlowRuns.AnyAsync(
                    value =>
                        value.FlowId == flow.Id
                        && value.SourceDefinitionId == context.Event.SourceDefinitionId.Value
                        && value.SourceNodeId == source.Id
                        && value.SourceOccurrenceId == context.Event.OccurrenceId,
                    cancellationToken
                );
                if (duplicate)
                {
                    duplicateCount++;
                }
                else
                {
                    blockedCount++;
                }

                continue;
            }

            _ = db.AutomationNodeRuns.Add(
                new()
                {
                    RunId = runId,
                    NodeId = source.Id,
                    Sequence = 0,
                    Status = AutomationNodeRunStatus.Succeeded,
                    AvailableAtUtc = now,
                    StartedAtUtc = now,
                    CompletedAtUtc = now,
                    OutcomeCode = "source-received",
                }
            );
            var next = Outgoing(flow.Edges, source.Id, null);
            AddPending(db, runId, next, now, 1);
            if (next.Length == 0)
            {
                var run = await db.AutomationFlowRuns.SingleAsync(
                    value => value.Id == runId,
                    cancellationToken
                );
                run.Status = AutomationFlowRunStatus.Completed;
                run.CompletedAtUtc = now;
            }

            _ = await db.SaveChangesAsync(cancellationToken);
            accepted.Add(new(runId));
        }

        if (accepted.Count == 0)
        {
            return Dispatched(
                duplicateCount == matching.Count ? AutomationDispatchStatus.Duplicate
                : blockedCount > 0 ? AutomationDispatchStatus.FeatureDisabled
                : AutomationDispatchStatus.NoMatchingFlow
            );
        }

        if (
            claim is not null
            && await claim(db, cancellationToken) is CustomCommandInvocationClaimOutcome.AlreadyUsed
        )
        {
            return new CustomCommandAutomationAdmissionOutcome.AlreadyUsed();
        }

        await dispatchTransaction.CommitAsync(cancellationToken);
        onCommitted?.Invoke();

        foreach (var runId in accepted)
        {
            _ = await ResumeAsync(runId, cancellationToken);
        }

        return new CustomCommandAutomationAdmissionOutcome.Dispatched(
            new(AutomationDispatchStatus.Accepted, accepted.ToImmutable())
        );

        static CustomCommandAutomationAdmissionOutcome Dispatched(
            AutomationDispatchStatus status
        ) => new CustomCommandAutomationAdmissionOutcome.Dispatched(new(status, []));
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
    )
    {
        var gate = await AdmissionGateAsync(hostId, cancellationToken);
        if (gate is not AutomationRuntimeGate.Enabled)
        {
            return new HashSet<string>(StringComparer.Ordinal);
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
        var definitionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flow in flows)
        {
            if (!await ValidFlowAsync(flow, hostId, cancellationToken))
            {
                continue;
            }

            foreach (var node in flow.Nodes.Where(IsSourceDefinition))
            {
                _ = definitionIds.Add(node.DefinitionId);
            }
        }

        return definitionIds;
    }

    public async Task<AutomationResumeOutcome> ResumeAsync(
        AutomationRunId runId,
        CancellationToken cancellationToken
    )
    {
        await EnsureInitializedAsync(cancellationToken);
        var claim = await ClaimRunAsync(runId, cancellationToken);
        AutomationResumeOutcome outcome;
        if (claim is AutomationRunClaim.Unavailable unavailable)
        {
            outcome = new(unavailable.Status);
        }
        else
        {
            var leaseId = ((AutomationRunClaim.Owned)claim).LeaseId;
            try
            {
                outcome = await ResumeOwnedAsync(runId, leaseId, cancellationToken);
            }
            finally
            {
                await ReleaseRunAsync(runId, leaseId, CancellationToken.None);
            }
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
                value.Status == AutomationNodeRunStatus.Pending && value.AvailableAtUtc <= now
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

            var now = clock.GetUtcNow().UtcDateTime;
            var pending = run
                .NodeRuns.Where(static value => value.Status == AutomationNodeRunStatus.Pending)
                .OrderBy(static value => value.Sequence)
                .FirstOrDefault();
            if (pending is null)
            {
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
                FailMalformedRun(run, now);
                _ = await db.SaveChangesAsync(cancellationToken);
                return new(AutomationResumeStatus.Failed);
            }

            var flow = restoredDefinition.Flow;
            var validation = await flowService.ValidateFrozenDefinitionAsync(
                new(run.HostId),
                flow,
                cancellationToken
            );
            var node = flow.Nodes.SingleOrDefault(value => value.Id == pending.NodeId);
            if (validation.Gate is not null || !validation.Errors.IsEmpty || node is null)
            {
                FailMalformedRun(run, now);
                _ = await db.SaveChangesAsync(cancellationToken);
                return new(AutomationResumeStatus.Failed);
            }

            var scope = new AutomationNodeExecutionScope(db, run, pending, node, flow, leaseId);
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

            var check = await catalog.ValidatePersistedBeforeExecutionAsync(
                new(run.HostId),
                available.Context,
                AutomationRuntimeSerialization.Definition(node),
                cancellationToken
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
            if (inputs is not AutomationInputResolution.Available resolvedInputs)
            {
                executionGate = await EnforceExecutionGateAsync(
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

                if (await StopsFlowAsync(scope, "input-resolution-failed", cancellationToken))
                {
                    return new(AutomationResumeStatus.Failed);
                }

                continue;
            }

            var result = await ExecuteNodeAsync(
                new(run.HostId),
                valid.Configuration,
                node,
                available.Context,
                resolvedInputs.FieldValues,
                cancellationToken
            );
            executionGate = await EnforceExecutionGateAsync(db, run, leaseId, cancellationToken);
            if (executionGate == AutomationExecutionGate.OwnershipLost)
            {
                continue;
            }
            if (executionGate != AutomationExecutionGate.Open)
            {
                return ExecutionBlocked(executionGate);
            }

            if (result is AutomationNodeExecution.Failed failure)
            {
                if (await StopsFlowAsync(scope, failure.Code, cancellationToken))
                {
                    return new(AutomationResumeStatus.Failed);
                }

                continue;
            }

            if (
                !await CompleteSuccessAsync(
                    scope,
                    (AutomationNodeExecution.Succeeded)result,
                    cancellationToken
                )
            )
            {
                continue;
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

    private async Task RecoverInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runs = await db
            .AutomationFlowRuns.Include(static value => value.NodeRuns)
            .Where(value =>
                value.ExecutionLeaseId != null
                || value.NodeRuns.Any(node => node.Status == AutomationNodeRunStatus.Running)
            )
            .ToArrayAsync(cancellationToken);
        if (runs.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var hostIds = runs.Select(static value => value.HostId).Distinct().ToArray();
        var hosts = await db
            .Hosts.AsNoTracking()
            .Where(value => hostIds.Contains(value.Id))
            .ToDictionaryAsync(
                static value => value.Id,
                static value => new { value.EnabledFeatures, value.AutomationGeneration },
                cancellationToken
            );
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var run in runs)
        {
            run.ExecutionLeaseId = null;
            var interrupted = run
                .NodeRuns.Where(static node => node.Status == AutomationNodeRunStatus.Running)
                .ToArray();
            if (interrupted.Length == 0)
            {
                continue;
            }

            if (Terminal(run.Status) is not null)
            {
                FinishInterruptedNodes(interrupted, run.Status, now);
                continue;
            }

            if (!hosts.TryGetValue(run.HostId, out var host))
            {
                Invalidate(run, now, "host-unavailable");
                continue;
            }

            if (host.AutomationGeneration != run.AutomationGeneration)
            {
                Invalidate(run, now, "automation-stale");
                continue;
            }

            if (
                !host.EnabledFeatures.Contains(
                    AutomationRequiredFeatures.BackingFeatures(run.RequiredFeatures)
                )
            )
            {
                Invalidate(run, now, "required-feature-disabled");
                continue;
            }

            if (
                AutomationRuntimeSerialization.RestoreDefinition(run.DefinitionJson)
                is not AutomationDefinitionRestoreOutcome.Available restoredDefinition
            )
            {
                FailMalformedRun(run, now);
                continue;
            }

            var flow = restoredDefinition.Flow;
            var definitions = flow.Nodes.ToDictionary(static node => node.Id);
            if (interrupted.Any(node => !definitions[node.NodeId].ContinueOnFailure))
            {
                FailInterruptedRun(run, interrupted, definitions, now);
                continue;
            }

            ContinueInterruptedRun(db, run, interrupted, definitions, flow, now);
        }

        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
                node.Status is AutomationNodeRunStatus.Pending or AutomationNodeRunStatus.Running
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
                value.Status == AutomationNodeRunStatus.Pending
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
                var next in Outgoing(flow.Edges, definitions[node.NodeId].Id, "complete")
                    .Where(existingNodes.Add)
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
        CancellationToken cancellationToken
    )
    {
        var leaseId = Guid.NewGuid();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
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
        var next = Outgoing(flow.Edges, node.Id, succeeded.OutputPort);
        var existingNodes = run.NodeRuns.Select(static value => value.NodeId).ToHashSet();
        AddPending(
            db,
            run.Id,
            next.Where(existingNodes.Add).ToImmutableArray(),
            succeeded.NextAvailableAtUtc,
            run.NodeRuns.Max(static value => value.Sequence) + 1
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> StopsFlowAsync(
        AutomationNodeExecutionScope scope,
        string code,
        CancellationToken cancellationToken
    ) =>
        await CompleteFailureAsync(scope, code, cancellationToken)
        == AutomationFailureCompletion.Stopped;

    private async Task<AutomationFailureCompletion> CompleteFailureAsync(
        AutomationNodeExecutionScope scope,
        string code,
        CancellationToken cancellationToken
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
        if (!node.ContinueOnFailure)
        {
            nodeRun.Status = AutomationNodeRunStatus.Failed;
            run.Status = AutomationFlowRunStatus.Failed;
            run.CompletedAtUtc = now;
            foreach (
                var pending in run.NodeRuns.Where(static value =>
                    value.Status == AutomationNodeRunStatus.Pending
                )
            )
            {
                pending.Status = AutomationNodeRunStatus.Failed;
                pending.OutcomeCode = "flow-stopped";
                pending.CompletedAtUtc = now;
            }

            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AutomationFailureCompletion.Stopped;
        }

        nodeRun.Status = AutomationNodeRunStatus.ContinuedAfterFailure;
        var next = Outgoing(flow.Edges, node.Id, "complete");
        var existingNodes = run.NodeRuns.Select(static value => value.NodeId).ToHashSet();
        AddPending(
            db,
            run.Id,
            next.Where(existingNodes.Add).ToImmutableArray(),
            now,
            run.NodeRuns.Max(static value => value.Sequence) + 1
        );
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
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
            ConditionControlConfiguration => EvaluateCondition(inputs),
            DelayControlConfiguration delay => Delay(delay),
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
        return outcome is AutomationActionOutcome.Failed failed
            ? new AutomationNodeExecution.Failed(failed.Code)
            : new AutomationNodeExecution.Succeeded(
                "plugin-succeeded",
                "complete",
                clock.GetUtcNow().UtcDateTime
            );
    }

    private AutomationNodeExecution Delay(DelayControlConfiguration configuration)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return configuration.Duration <= DateTime.MaxValue - now
            ? new AutomationNodeExecution.Succeeded("delayed", null, now + configuration.Duration)
            : new AutomationNodeExecution.Failed("delay-unrepresentable");
    }

    private AutomationNodeExecution EvaluateCondition(
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationResolvedValue> inputs
    ) =>
        inputs.GetValueOrDefault(new("predicate"))?.Value switch
        {
            AutomationValue.Boolean { Value: var result } => new AutomationNodeExecution.Succeeded(
                result ? "condition-true" : "condition-false",
                result ? "yes" : "no",
                clock.GetUtcNow().UtcDateTime
            ),
            _ => new AutomationNodeExecution.Failed("condition-invalid"),
        };

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
            AutomationRuntimeSerialization.RestoreInputBindings(node.InputBindingsJson)
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
        return outcome switch
        {
            AutomationActionOutcome.Succeeded => new AutomationNodeExecution.Succeeded(
                "action-succeeded",
                "complete",
                clock.GetUtcNow().UtcDateTime
            ),
            AutomationActionOutcome.Failed failed => new AutomationNodeExecution.Failed(
                failed.Code
            ),
            _ => new AutomationNodeExecution.Failed("action-failed"),
        };
    }

    private static void Invalidate(AutomationFlowRun run, DateTime now, string outcomeCode)
    {
        run.Status = AutomationFlowRunStatus.Invalidated;
        run.CompletedAtUtc = now;
        foreach (
            var node in run.NodeRuns.Where(static value =>
                value.Status is AutomationNodeRunStatus.Pending or AutomationNodeRunStatus.Running
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

    private static ImmutableArray<Guid> Outgoing(
        IEnumerable<AutomationFlowEdge> edges,
        Guid sourceNodeId,
        string? sourcePort
    ) =>
        edges
            .Where(edge =>
                edge.Kind == PersistedAutomationEdgeKind.Flow
                && edge.SourceNodeId == sourceNodeId
                && (sourcePort is null || edge.SourcePortId == sourcePort)
            )
            .OrderBy(static edge => edge.SourcePortId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetNodeId)
            .Select(static edge => edge.TargetNodeId)
            .ToImmutableArray();

    private static ImmutableArray<Guid> Outgoing(
        IEnumerable<AutomationRuntimeSerialization.PersistedEdge> edges,
        Guid sourceNodeId,
        string? sourcePort
    ) =>
        edges
            .Where(edge =>
                edge.Kind == AutomationEdgeKind.Flow
                && edge.SourceNodeId == sourceNodeId
                && (sourcePort is null || edge.SourcePortId == sourcePort)
            )
            .OrderBy(static edge => edge.SourcePortId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetNodeId)
            .Select(static edge => edge.TargetNodeId)
            .ToImmutableArray();

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

        internal sealed record Owned(Guid LeaseId) : AutomationRunClaim;

        internal sealed record Unavailable(AutomationResumeStatus Status) : AutomationRunClaim;
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
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        public async ValueTask FailAsync(
            AutomationRuntimeSerialization.PersistedNode node,
            string code,
            CancellationToken cancellationToken
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

    private abstract record AutomationNodeExecution
    {
        private AutomationNodeExecution() { }

        internal sealed record Succeeded(
            string Code,
            string? OutputPort,
            DateTime NextAvailableAtUtc
        ) : AutomationNodeExecution;

        internal sealed record Failed(string Code) : AutomationNodeExecution;
    }
}
