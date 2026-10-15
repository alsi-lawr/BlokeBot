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
        var rejections = ImmutableArray.CreateBuilder<AutomationAdmissionRejection>();
        var accepted = ImmutableArray.CreateBuilder<AutomationRunId>();
        var duplicateCount = 0;
        var matchingCount = 0;
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == context.HostId.Value)
            .Select(value => new { value.EnabledFeatures, value.AutomationGeneration })
            .SingleAsync(cancellationToken);
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.Automations))
        {
            return Dispatch(AutomationDispatchStatus.FeatureDisabled);
        }
        if (currentOccurrence is not null && !await currentOccurrence(db, cancellationToken))
        {
            return Dispatch(AutomationDispatchStatus.Duplicate);
        }
        var flows = await db
            .AutomationFlows.AsNoTracking()
            .Include(flow => flow.Nodes)
            .Include(flow => flow.Edges)
            .Where(flow =>
                flow.HostId == context.HostId.Value
                && (selectedFlow == null || flow.Id == selectedFlow.Value.Value)
                && flow.IsEnabled
                && flow.SchemaVersion == AutomationFlowSchema.CurrentVersion
            )
            .OrderBy(flow => flow.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var flow in flows)
        {
            var potentialSources = flow
                .Nodes.Where(node =>
                    node.DefinitionId == context.Event.SourceDefinitionId.Value
                    && IsSourceDefinition(node)
                    && (selectedSource == null || node.Id == selectedSource.Value.Value)
                )
                .ToImmutableArray();
            if (potentialSources.IsEmpty)
            {
                continue;
            }
            if (
                AutomationFlowService.RestoreDraft(flow)
                is not AutomationFlowDraftRestoreOutcome.Available restored
            )
            {
                Reject(
                    flow,
                    potentialSources,
                    AutomationAdmissionRejectionReason.SourceReconstruction
                );
                continue;
            }
            var sources = ImmutableArray.CreateBuilder<AutomationFlowNode>();
            var invalidSource = false;
            foreach (var source in potentialSources)
            {
                if (
                    catalog.ValidatePersistedDefinition(Definition(source))
                    is not AutomationConfigurationCheck.Valid valid
                )
                {
                    invalidSource = true;
                }
                else if (matches(valid.Configuration))
                {
                    sources.Add(source);
                }
            }
            if (invalidSource)
            {
                Reject(
                    flow,
                    potentialSources,
                    AutomationAdmissionRejectionReason.SourceConfiguration
                );
                continue;
            }
            if (sources.Count == 0)
            {
                continue;
            }
            matchingCount += sources.Count;
            var preparation = await PrepareAdmissionAsync(
                db,
                context,
                flow,
                restored.Draft,
                sources.ToImmutable(),
                host.EnabledFeatures,
                cancellationToken
            );
            await preparation.Match(
                async prepared =>
                {
                    foreach (var candidate in prepared.Runs)
                    {
                        var runId = candidate.RunId.Value;
                        var sourceId = candidate.SourceNodeId.Value;
                        var frozenFlow = candidate.Flow;
                        var requiredFeatures = candidate.RequiredFeatures;
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
                                sourceId,
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
                                    && value.SourceDefinitionId
                                        == context.Event.SourceDefinitionId.Value
                                    && value.SourceNodeId == sourceId
                                    && value.SourceOccurrenceId == context.Event.OccurrenceId,
                                cancellationToken
                            );
                            if (duplicate)
                            {
                                duplicateCount++;
                            }
                            else
                            {
                                Reject(
                                    flow,
                                    [.. sources],
                                    AutomationAdmissionRejectionReason.FeatureDisabled
                                );
                            }

                            continue;
                        }

                        _ = db.AutomationNodeRuns.Add(
                            new()
                            {
                                RunId = runId,
                                NodeId = sourceId,
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
                                    prepared.Closure,
                                    cancellationToken
                                )
                            ).IsEmpty
                        )
                        {
                            throw new InvalidOperationException(
                                "Prepared admission violated its locked reference invariants."
                            );
                        }
                        var traceFlow = (
                            (AutomationDefinitionRestoreOutcome.Available)
                                AutomationRuntimeSerialization.RestoreDefinition(
                                    traceRun.DefinitionJson
                                )
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
                            traceFlow.Nodes.Single(node => node.Id == sourceId),
                            AutomationTraceOutcome.Succeeded
                        );
                        var next = AutomationFlowTraversal.Outgoing(
                            traceFlow.Edges,
                            sourceId,
                            new AutomationOutputSelection.All()
                        );
                        AddPending(db, runId, next, now, 1);
                        await TraceScheduledAsync(
                            db,
                            traceRun,
                            traceFlow,
                            next,
                            now,
                            cancellationToken
                        );
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
                },
                rejected =>
                {
                    Reject(flow, sources.ToImmutable(), rejected.Reason);
                    return Task.CompletedTask;
                }
            );
        }
        var status =
            accepted.Count > 0 ? AutomationDispatchStatus.Accepted
            : rejections.Any(rejection =>
                rejection.Reason != AutomationAdmissionRejectionReason.FeatureDisabled
            )
                ? AutomationDispatchStatus.InvalidFlow
            : matchingCount > 0 && duplicateCount == matchingCount
                ? AutomationDispatchStatus.Duplicate
            : rejections.Count > 0 ? AutomationDispatchStatus.FeatureDisabled
            : AutomationDispatchStatus.NoMatchingFlow;
        return Dispatch(status);

        AutomationDispatchOutcome Dispatch(AutomationDispatchStatus status) =>
            new(status, accepted.ToImmutable()) { Rejections = rejections.ToImmutable() };

        void Reject(
            AutomationFlow flow,
            ImmutableArray<AutomationFlowNode> sources,
            AutomationAdmissionRejectionReason reason
        )
        {
            var rejection = new AutomationAdmissionRejection(
                new(flow.Id),
                [.. sources.Select(source => new AutomationNodeId(source.Id))],
                reason
            );
            rejections.Add(rejection);
            ReportAdmissionRejection(context.HostId, rejection);
        }
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
