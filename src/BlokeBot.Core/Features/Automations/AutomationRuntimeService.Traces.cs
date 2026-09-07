using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    private Task TraceAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        AutomationTraceEventKind kind,
        CancellationToken cancellationToken,
        AutomationRuntimeSerialization.PersistedNode? node = null,
        AutomationTraceOutcome outcome = AutomationTraceOutcome.None,
        string? port = null,
        DateTime? due = null,
        IReadOnlyDictionary<AutomationPortId, AutomationResolvedValue>? values = null
    ) =>
        AutomationTraceStore.AppendAsync(
            db,
            run.Id,
            AutomationTraceRedaction.Event(
                run.Id,
                kind,
                clock.GetUtcNow().UtcDateTime,
                node,
                outcome,
                port,
                due,
                values
            ),
            clock.GetUtcNow().UtcDateTime,
            cancellationToken
        );

    private async Task TraceScheduledAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        AutomationRuntimeSerialization.PersistedFlow flow,
        IEnumerable<Guid> nodes,
        DateTime due,
        CancellationToken cancellationToken
    )
    {
        foreach (var id in nodes)
        {
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.Scheduled,
                cancellationToken,
                flow.Nodes.Single(node => node.Id == id),
                due: due
            );
        }
    }

    private async Task TraceCancellationAsync(
        AutomationRunId id,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var run = await db
            .AutomationFlowRuns.AsNoTracking()
            .SingleOrDefaultAsync(run => run.Id == id.Value, cancellationToken);
        if (run is not null)
        {
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.Cancellation,
                cancellationToken,
                outcome: AutomationTraceOutcome.Cancelled
            );
        }
    }
}
