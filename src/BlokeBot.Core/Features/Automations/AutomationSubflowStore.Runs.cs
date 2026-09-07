using System.Collections.Immutable;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationSubflowRunReferences
{
    // The runtime supplies its admission transaction; this contract never commits or creates runs.
    internal static async Task<ImmutableArray<AutomationGraphError>> AttachAsync(
        BlokeBotDbContext db,
        AutomationHostId hostId,
        AutomationRunId runId,
        AutomationSubflowClosure closure,
        CancellationToken cancellationToken
    )
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Attach subflow references inside the runtime admission transaction."
            );
        }
        if (await MainDatabaseStatements.LockHostAsync(db, hostId.Value, cancellationToken) == 0)
        {
            return
            [
                AutomationSubflowStore.Error(
                    "subflow-run-host",
                    "Attach the closure to a run owned by this host."
                ),
            ];
        }
        var run =
            db.AutomationFlowRuns.Local.SingleOrDefault(row => row.Id == runId.Value)
            ?? await db.AutomationFlowRuns.SingleOrDefaultAsync(
                row => row.Id == runId.Value,
                cancellationToken
            );
        if (run is null || run.HostId != hostId.Value)
        {
            return
            [
                AutomationSubflowStore.Error(
                    "subflow-run-host",
                    "Attach the closure to a run owned by this host."
                ),
            ];
        }
        var ids = closure.Revisions.Select(revision => revision.Id.Value).ToArray();
        var rows = await db
            .AutomationSubflowRevisions.AsNoTracking()
            .Where(row => row.HostId == hostId.Value && ids.Contains(row.Id))
            .Select(row => row.SnapshotJson)
            .ToArrayAsync(cancellationToken);
        var authoritative = new AutomationSubflowClosure([
            .. rows.Select(AutomationSubflowSerialization.Restore),
        ]);
        if (
            closure.Revisions.Select(revision => revision.SubflowId).Distinct().Count()
                != closure.Revisions.Length
            || closure.Revisions.Any(revision =>
                !AutomationSubflowStore.ValidateCalls(revision.Graph, closure).IsEmpty
            )
            || authoritative.Revisions.Length != closure.Revisions.Length
            || closure.Revisions.Any(revision =>
                !authoritative.Revisions.Any(stored =>
                    stored.Id == revision.Id
                    && AutomationSubflowSerialization.Serialize(stored)
                        == AutomationSubflowSerialization.Serialize(revision)
                )
            )
        )
        {
            return
            [
                AutomationSubflowStore.Error(
                    "subflow-closure-invalid",
                    "Attach the complete immutable revision closure."
                ),
            ];
        }
        var existing = await db
            .AutomationSubflowRunReferences.Where(row => row.RunId == runId.Value)
            .Select(row => row.RevisionId)
            .ToArrayAsync(cancellationToken);
        if (
            existing.Length > 0
            && !existing
                .ToHashSet()
                .SetEquals(closure.Revisions.Select(revision => revision.Id.Value))
        )
        {
            return
            [
                AutomationSubflowStore.Error(
                    "subflow-closure-immutable",
                    "An admitted run cannot change its frozen closure."
                ),
            ];
        }
        if (existing.Length == 0)
        {
            db.AutomationSubflowRunReferences.AddRange(
                closure.Revisions.Select(revision => new AutomationSubflowRunReference
                {
                    RunId = runId.Value,
                    HostId = hostId.Value,
                    RevisionId = revision.Id.Value,
                })
            );
        }
        return [];
    }

    internal static async Task<AutomationSubflowClosure> LoadAsync(
        BlokeBotDbContext db,
        AutomationHostId hostId,
        AutomationRunId runId,
        CancellationToken cancellationToken
    )
    {
        var rows = await (
            from reference in db.AutomationSubflowRunReferences.AsNoTracking()
            join revision in db.AutomationSubflowRevisions.AsNoTracking()
                on new { reference.HostId, Id = reference.RevisionId } equals new
                {
                    revision.HostId,
                    revision.Id,
                }
            where reference.HostId == hostId.Value && reference.RunId == runId.Value
            select revision.SnapshotJson
        ).ToArrayAsync(cancellationToken);
        return new([.. rows.Select(AutomationSubflowSerialization.Restore)]);
    }

    internal static Task<int> RetireAsync(
        BlokeBotDbContext db,
        AutomationHostId hostId,
        AutomationRunId runId,
        CancellationToken cancellationToken
    ) =>
        db
            .AutomationSubflowRunReferences.Where(row =>
                row.HostId == hostId.Value && row.RunId == runId.Value
            )
            .ExecuteDeleteAsync(cancellationToken);
}
