using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    private async Task<AutomationDispatchOutcome> AdmitAsync(
        BlokeBotDbContext db,
        AutomationContext context,
        Func<AutomationConfiguration, bool> matches,
        CancellationToken cancellationToken,
        Func<BlokeBotDbContext, CancellationToken, Task<bool>>? currentOccurrence = null,
        AutomationFlowId? selectedFlow = null,
        AutomationNodeId? selectedSource = null
    )
    {
        var flows = await db
            .AutomationFlows.AsNoTracking()
            .Include(static flow => flow.Nodes)
            .Include(static flow => flow.Edges)
            .Where(flow =>
                flow.HostId == context.HostId.Value
                && (selectedFlow == null || flow.Id == selectedFlow.Value.Value)
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
                    && (selectedSource == null || node.Id == selectedSource.Value.Value)
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

            foreach (var source in matchingSources)
            {
                matching.Add((flow, source));
            }
        }

        var accepted = ImmutableArray.CreateBuilder<AutomationRunId>();
        var duplicateCount = 0;
        var blockedCount = 0;

        var host = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == context.HostId.Value)
            .Select(static value => new { value.EnabledFeatures, value.AutomationGeneration })
            .SingleAsync(cancellationToken);
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return Dispatch(AutomationDispatchStatus.FeatureDisabled);
        }

        if (currentOccurrence is not null && !await currentOccurrence(db, cancellationToken))
        {
            return Dispatch(AutomationDispatchStatus.Duplicate);
        }

        if (invalidFlow)
        {
            return Dispatch(AutomationDispatchStatus.InvalidFlow);
        }

        if (matching.Count == 0)
        {
            return Dispatch(AutomationDispatchStatus.NoMatchingFlow);
        }

        var drafts = matching
            .Select(item => item.Flow)
            .DistinctBy(flow => flow.Id)
            .Select(flow =>
                (
                    (AutomationFlowDraftRestoreOutcome.Available)
                        AutomationFlowService.RestoreDraft(flow)
                ).Draft
            )
            .ToArray();
        var resolved = await AutomationSubflowStore.LoadClosureAsync(
            db,
            context.HostId,
            drafts
                .SelectMany(draft => AutomationSubflowStore.Calls(draft.Nodes))
                .Select(call => call.SubflowId),
            null,
            cancellationToken
        );
        if (resolved is not AutomationSubflowClosureOutcome.Available current)
        {
            return Dispatch(AutomationDispatchStatus.InvalidFlow);
        }
        foreach (var draft in drafts)
        {
            var validation = await flowService.ValidatePreparedAsync(
                draft,
                current.Closure,
                AutomationFlowService.AutomationGraphAdmission.Saved,
                cancellationToken,
                db
            );
            if (
                validation.Gate is not null
                || validation.Errors.Any(error => error.Code != "capability-unavailable")
            )
            {
                return Dispatch(AutomationDispatchStatus.InvalidFlow);
            }
        }

        foreach (var (flow, source) in matching)
        {
            var runId = Guid.NewGuid();
            var original = (
                (AutomationDefinitionRestoreOutcome.Available)
                    AutomationRuntimeSerialization.RestoreDefinition(
                        AutomationRuntimeSerialization.SerializeDefinition(flow)
                    )
            ).Flow;
            var frozen = AutomationFrozenSubflows.Freeze(original, runId, current.Closure);
            if (frozen is null || FreezeExecutionFences(frozen.Flow) is not { } frozenFlow)
            {
                return Dispatch(AutomationDispatchStatus.InvalidFlow);
            }
            var requiredFeatures = AutomationRequiredFeatures.ForDefinitions(
                frozenFlow.Nodes.Select(static node => node.DefinitionId)
            );
            if (!host.EnabledFeatures.Contains(requiredFeatures))
            {
                blockedCount++;
                continue;
            }

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
                    AutomationRuntimeSerialization.SerializeDefinition(frozenFlow),
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
            var traceRun = await db.AutomationFlowRuns.SingleAsync(
                value => value.Id == runId,
                cancellationToken
            );
            if (
                !(
                    await AutomationSubflowRunReferences.AttachAsync(
                        db,
                        new(flow.HostId),
                        new(runId),
                        frozen.Closure,
                        cancellationToken
                    )
                ).IsEmpty
            )
            {
                return Dispatch(AutomationDispatchStatus.InvalidFlow);
            }
            var traceFlow = (
                (AutomationDefinitionRestoreOutcome.Available)
                    AutomationRuntimeSerialization.RestoreDefinition(traceRun.DefinitionJson)
            ).Flow;
            await AutomationTraceStore.CreateAsync(
                db,
                runId,
                flow.HostId,
                flow.Id,
                runId,
                now,
                cancellationToken
            );
            await TraceAsync(
                db,
                traceRun,
                AutomationTraceEventKind.Admission,
                cancellationToken,
                traceFlow.Nodes.Single(node => node.Id == source.Id),
                AutomationTraceOutcome.Succeeded
            );
            var next = AutomationFlowTraversal.Outgoing(
                traceFlow.Edges,
                source.Id,
                new AutomationOutputSelection.All()
            );
            AddPending(db, runId, next, now, 1);
            await TraceScheduledAsync(db, traceRun, traceFlow, next, now, cancellationToken);
            if (next.Length == 0)
            {
                var run = await db.AutomationFlowRuns.SingleAsync(
                    value => value.Id == runId,
                    cancellationToken
                );
                run.Status = AutomationFlowRunStatus.Completed;
                run.CompletedAtUtc = now;
                await CompleteTerminalAsync(
                    db,
                    run,
                    AutomationTraceOutcome.Succeeded,
                    cancellationToken
                );
            }

            _ = await db.SaveChangesAsync(cancellationToken);
            accepted.Add(new(runId));
        }

        return accepted.Count == 0
            ? Dispatch(
                duplicateCount == matching.Count ? AutomationDispatchStatus.Duplicate
                : blockedCount > 0 ? AutomationDispatchStatus.FeatureDisabled
                : AutomationDispatchStatus.NoMatchingFlow
            )
            : new(AutomationDispatchStatus.Accepted, accepted.ToImmutable());

        static AutomationDispatchOutcome Dispatch(AutomationDispatchStatus status) =>
            new(status, []);
    }

    internal async Task DispatchObservedAsync(
        AutomationHostId hostId,
        Func<
            BlokeBotDbContext,
            CancellationToken,
            Task<ImmutableArray<AutomationObservedTrigger>>
        > observe,
        CancellationToken cancellationToken
    )
    {
        await EnsureInitializedAsync(cancellationToken);
        var accepted = ImmutableArray.CreateBuilder<AutomationRunId>();
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            if (
                await MainDatabaseStatements.LockHostAsync(db, hostId.Value, cancellationToken) == 0
            )
            {
                return;
            }
            foreach (var trigger in await observe(db, cancellationToken))
            {
                var admission = await AdmitAsync(
                    db,
                    trigger.Context,
                    trigger.Matches,
                    cancellationToken,
                    (current, token) =>
                        AcceptExpandedOccurrenceAsync(current, trigger.Context, token)
                );
                if (admission.Status == AutomationDispatchStatus.InvalidFlow)
                {
                    return;
                }
                accepted.AddRange(admission.RunIds);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        await RunAdmittedAsync(hostId, accepted.ToImmutable(), true, cancellationToken);
    }

    private async Task RunAdmittedAsync(
        AutomationHostId hostId,
        ImmutableArray<AutomationRunId> accepted,
        bool resumeImmediately,
        CancellationToken cancellationToken
    )
    {
        foreach (var runId in accepted)
        {
            await uiChanges.RunChangedAsync(hostId, runId);
        }
        if (resumeImmediately)
        {
            foreach (var runId in accepted)
            {
                _ = await ResumeAsync(runId, cancellationToken);
            }
        }
    }

    private static async Task<bool> AcceptExpandedOccurrenceAsync(
        BlokeBotDbContext db,
        AutomationContext context,
        CancellationToken cancellationToken
    )
    {
        var acceptAfter = await db
            .AutomationSourceAdmissions.Where(host => host.HostId == context.Channel.HostId.Value)
            .Select(host => (DateTime?)host.AcceptEventsAfterUtc)
            .SingleOrDefaultAsync(cancellationToken);
        return acceptAfter is null || context.Timestamps.OccurredAtUtc.UtcDateTime > acceptAfter;
    }
}

internal sealed record AutomationObservedTrigger(
    AutomationContext Context,
    Func<AutomationConfiguration, bool> Matches
);
