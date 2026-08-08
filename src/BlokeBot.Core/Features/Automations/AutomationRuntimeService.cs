using System.Collections.Immutable;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationRuntimeService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationCatalogService catalog,
    AutomationExpressionService expressions,
    AutomationActionExecutor actions,
    TimeProvider clock
)
{
    private readonly Lock _initializationGate = new();
    private Task? _initialization;

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

    private async Task<CustomCommandAutomationAdmissionOutcome> DispatchCoreAsync(
        AutomationTrigger trigger,
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
                flow.HostId == trigger.Context.HostId.Value
                && flow.IsEnabled
                && flow.SchemaVersion == AutomationFlowSchema.CurrentVersion
            )
            .OrderBy(static flow => flow.Id)
            .ToArrayAsync(cancellationToken);
        var matching = new List<(AutomationFlow Flow, AutomationFlowNode Source)>();
        foreach (var flow in flows)
        {
            var source = flow.Nodes.SingleOrDefault(node =>
                node.DefinitionId == trigger.Context.Event.SourceDefinitionId.Value
                && IsSource(flow, node)
            );
            if (source is null)
            {
                continue;
            }

            var check = catalog.ValidatePersistedDefinition(Definition(source));
            if (
                check is AutomationConfigurationCheck.Valid valid
                && Equals(valid.Configuration, trigger.SourceConfiguration)
            )
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
        var hostExists = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE hosts
            SET EnabledFeatures = EnabledFeatures
            WHERE Id = {trigger.Context.HostId.Value};
            """,
            cancellationToken
        );
        if (hostExists == 0)
        {
            return Dispatched(AutomationDispatchStatus.HostNotFound);
        }

        var host = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == trigger.Context.HostId.Value)
            .Select(static value => new { value.EnabledFeatures, value.AutomationGeneration })
            .SingleAsync(cancellationToken);
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return Dispatched(AutomationDispatchStatus.FeatureDisabled);
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
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT OR IGNORE INTO automation_flow_runs
                    (Id, FlowId, HostId, AutomationGeneration, RequiredFeatures,
                     ContextSchemaVersion, SourceDefinitionId, SourceOccurrenceId, ContextJson,
                     DefinitionJson, Status, StartedAtUtc, CompletedAtUtc, ExecutionLeaseId)
                VALUES
                    ({runId}, {flow.Id}, {flow.HostId}, {host.AutomationGeneration},
                     {(long)requiredFeatures}, {AutomationContextSchema.CurrentVersion},
                     {trigger.Context.Event.SourceDefinitionId.Value},
                     {trigger.Context.Event.OccurrenceId},
                     {AutomationRuntimeSerialization.SerializeContext(trigger.Context)},
                     {AutomationRuntimeSerialization.SerializeDefinition(flow)},
                     {"Running"}, {now}, NULL, NULL);
                """,
                cancellationToken
            );
            if (inserted == 0)
            {
                var duplicate = await db.AutomationFlowRuns.AnyAsync(
                    value =>
                        value.FlowId == flow.Id
                        && value.SourceDefinitionId
                            == trigger.Context.Event.SourceDefinitionId.Value
                        && value.SourceOccurrenceId == trigger.Context.Event.OccurrenceId,
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
        var gate = await GateAsync(hostId, cancellationToken);
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
            foreach (
                var source in flow.Nodes.Where(node =>
                    node.DefinitionId == AutomationDefinitionIds.CustomCommandSource.Value
                    && IsSource(flow, node)
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

    public async Task<AutomationResumeOutcome> ResumeAsync(
        AutomationRunId runId,
        CancellationToken cancellationToken
    )
    {
        await EnsureInitializedAsync(cancellationToken);
        var claim = await ClaimRunAsync(runId, cancellationToken);
        if (claim is AutomationRunClaim.Unavailable unavailable)
        {
            return new(unavailable.Status);
        }

        var leaseId = ((AutomationRunClaim.Owned)claim).LeaseId;
        try
        {
            return await ResumeOwnedAsync(runId, leaseId, cancellationToken);
        }
        finally
        {
            await ReleaseRunAsync(runId, leaseId, CancellationToken.None);
        }
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

            var gate = await GateAsync(new(run.HostId), cancellationToken);
            if (gate is not AutomationRuntimeGate.Enabled enabled)
            {
                return new(
                    gate is AutomationRuntimeGate.HostNotFound
                        ? AutomationResumeStatus.NotFound
                        : AutomationResumeStatus.FeatureDisabled
                );
            }

            if (run.AutomationGeneration != enabled.Generation)
            {
                if (await InvalidateOwnedAsync(db, run, leaseId, cancellationToken))
                {
                    return new(AutomationResumeStatus.Invalidated);
                }

                continue;
            }

            if (!enabled.Features.Contains(run.RequiredFeatures))
            {
                return new(AutomationResumeStatus.FeatureDisabled);
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

            var afterClaim = await GateAsync(new(run.HostId), cancellationToken);
            if (
                afterClaim is not AutomationRuntimeGate.Enabled current
                || current.Generation != run.AutomationGeneration
                || !current.Features.Contains(run.RequiredFeatures)
            )
            {
                return new(
                    afterClaim is AutomationRuntimeGate.Enabled
                        ? AutomationResumeStatus.Invalidated
                        : AutomationResumeStatus.FeatureDisabled
                );
            }

            var flow = AutomationRuntimeSerialization.DeserializeDefinition(run.DefinitionJson);
            var node = flow.Nodes.Single(value => value.Id == pending.NodeId);
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

            var result = await ExecuteNodeAsync(
                new(run.HostId),
                valid.Configuration,
                node,
                available.Context,
                cancellationToken
            );
            var finalGate = await GateAsync(new(run.HostId), cancellationToken);
            if (
                finalGate is not AutomationRuntimeGate.Enabled finalEnabled
                || finalEnabled.Generation != run.AutomationGeneration
                || !finalEnabled.Features.Contains(run.RequiredFeatures)
            )
            {
                return new(
                    finalGate is AutomationRuntimeGate.Enabled
                        ? AutomationResumeStatus.Invalidated
                        : AutomationResumeStatus.FeatureDisabled
                );
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

            if (
                !hosts.TryGetValue(run.HostId, out var host)
                || host.AutomationGeneration != run.AutomationGeneration
                || !host.EnabledFeatures.Contains(run.RequiredFeatures)
            )
            {
                Invalidate(run, now);
                continue;
            }

            var flow = AutomationRuntimeSerialization.DeserializeDefinition(run.DefinitionJson);
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
                    ? "automation-disabled"
                    : "execution-interrupted";
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
        AddPending(
            db,
            run.Id,
            next,
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
        AddPending(db, run.Id, next, now, run.NodeRuns.Max(static value => value.Sequence) + 1);
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AutomationFailureCompletion.Continued;
    }

    private async Task<bool> InvalidateOwnedAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        Guid leaseId,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        Invalidate(run, clock.GetUtcNow().UtcDateTime);
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
        CancellationToken cancellationToken
    ) =>
        configuration switch
        {
            ConditionControlConfiguration condition => EvaluateCondition(condition, context),
            DelayControlConfiguration delay => Delay(delay),
            _ => await ExecuteActionAsync(hostId, configuration, node, context, cancellationToken),
        };

    private AutomationNodeExecution Delay(DelayControlConfiguration configuration)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return configuration.Duration <= DateTime.MaxValue - now
            ? new AutomationNodeExecution.Succeeded("delayed", null, now + configuration.Duration)
            : new AutomationNodeExecution.Failed("delay-unrepresentable");
    }

    private AutomationNodeExecution EvaluateCondition(
        ConditionControlConfiguration condition,
        AutomationContext context
    ) =>
        expressions.Evaluate(
            new(AutomationExpressionLanguage.CurrentVersion, condition.Expression),
            context
        ) switch
        {
            AutomationExpressionResult.Value { Result: bool result } =>
                new AutomationNodeExecution.Succeeded(
                    result ? "condition-true" : "condition-false",
                    result ? "true" : "false",
                    clock.GetUtcNow().UtcDateTime
                ),
            _ => new AutomationNodeExecution.Failed("condition-invalid"),
        };

    private async Task<AutomationNodeExecution> ExecuteActionAsync(
        AutomationHostId hostId,
        AutomationConfiguration configuration,
        AutomationRuntimeSerialization.PersistedNode node,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        var outcome = await actions.ExecuteAsync(
            hostId,
            configuration,
            AutomationRuntimeSerialization.DeserializeExpressions(node.FieldExpressionsJson),
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

    private static void Invalidate(AutomationFlowRun run, DateTime now)
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
            node.OutcomeCode = "automation-disabled";
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
                edge.SourceNodeId == sourceNodeId
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
                edge.SourceNodeId == sourceNodeId
                && (sourcePort is null || edge.SourcePortId == sourcePort)
            )
            .OrderBy(static edge => edge.SourcePortId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetNodeId)
            .Select(static edge => edge.TargetNodeId)
            .ToImmutableArray();

    private static bool IsSource(AutomationFlow flow, AutomationFlowNode node) =>
        flow.Edges.All(edge => edge.TargetNodeId != node.Id);

    private static PersistedAutomationNodeDefinition Definition(AutomationFlowNode node) =>
        new(
            node.DefinitionId,
            node.DefinitionSchemaVersion,
            System.Text.Json.JsonDocument.Parse(node.ConfigurationJson).RootElement.Clone()
        );

    private async Task<AutomationRuntimeGate> GateAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId.Value)
            .Select(static value => new { value.EnabledFeatures, value.AutomationGeneration })
            .SingleOrDefaultAsync(cancellationToken);
        return host switch
        {
            null => new AutomationRuntimeGate.HostNotFound(),
            { EnabledFeatures: var features }
                when !features.Contains(HostFeatureFlags.Automations) =>
                new AutomationRuntimeGate.Disabled(),
            _ => new AutomationRuntimeGate.Enabled(host.AutomationGeneration, host.EnabledFeatures),
        };
    }

    private static AutomationResumeStatus? Terminal(AutomationFlowRunStatus status) =>
        status switch
        {
            AutomationFlowRunStatus.Completed => AutomationResumeStatus.Completed,
            AutomationFlowRunStatus.Failed => AutomationResumeStatus.Failed,
            AutomationFlowRunStatus.Invalidated => AutomationResumeStatus.Invalidated,
            _ => null,
        };

    private abstract record AutomationRuntimeGate
    {
        private AutomationRuntimeGate() { }

        internal sealed record Enabled(int Generation, HostFeatureFlags Features)
            : AutomationRuntimeGate;

        internal sealed record Disabled : AutomationRuntimeGate;

        internal sealed record HostNotFound : AutomationRuntimeGate;
    }

    private abstract record AutomationRunClaim
    {
        private AutomationRunClaim() { }

        internal sealed record Owned(Guid LeaseId) : AutomationRunClaim;

        internal sealed record Unavailable(AutomationResumeStatus Status) : AutomationRunClaim;
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
