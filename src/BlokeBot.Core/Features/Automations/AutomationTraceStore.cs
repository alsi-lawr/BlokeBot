using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationTraceStore(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock
)
{
    public const int SchemaVersion = 1;
    public const int MaximumEvents = 1024;
    public const int MaximumBytes = 524_288;
    public const int CleanupBatchSize = 100;
    public const int QueryLimit = 50;
    public static TimeSpan Retention { get; } = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static async Task CreateAsync(
        BlokeBotDbContext db,
        Guid id,
        int hostId,
        Guid? flowId,
        Guid? runId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        _ = db.AutomationTraces.Add(
            new()
            {
                Id = id,
                HostId = hostId,
                FlowId = flowId,
                ProductionRunId = runId,
                SchemaVersion = SchemaVersion,
                CreatedAtUtc = now,
                ExpiresAtUtc = now + Retention,
            }
        );
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    internal static async Task AppendAsync(
        BlokeBotDbContext db,
        Guid id,
        AutomationTraceEventData data,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var json = JsonSerializer.Serialize(data, _json);
        var bytes = Encoding.UTF8.GetByteCount(json);
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var active = db.AutomationTraces.Where(trace =>
            trace.Id == id && trace.ExpiresAtUtc > now && trace.Truncation == 0
        );
        var appended = await active
            .Where(trace =>
                trace.EventCount < MaximumEvents && trace.ByteCount + bytes <= MaximumBytes
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(trace => trace.EventCount, trace => trace.EventCount + 1)
                        .SetProperty(trace => trace.ByteCount, trace => trace.ByteCount + bytes),
                cancellationToken
            );
        if (appended == 1)
        {
            var sequence = await db
                .AutomationTraces.Where(trace => trace.Id == id)
                .Select(trace => trace.EventCount)
                .SingleAsync(cancellationToken);
            _ = db.AutomationTraceEvents.Add(
                new()
                {
                    TraceId = id,
                    Sequence = sequence,
                    EventJson = json,
                }
            );
            _ = await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            _ = await active.ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(
                        trace => trace.Truncation,
                        trace =>
                            trace.EventCount >= MaximumEvents
                                ? (int)AutomationTraceTruncation.EventLimit
                                : (int)AutomationTraceTruncation.ByteLimit
                    ),
                cancellationToken
            );
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task<AutomationTraceReadOutcome> ReadAsync(
        AutomationHostId hostId,
        AutomationTraceId id,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // One SQL query keeps the counters and bounded prefix coherent with concurrent appends/expiry.
        var stored = await db
            .AutomationTraces.AsNoTracking()
            .AsSingleQuery()
            .Where(trace => trace.Id == id.Value && trace.HostId == hostId.Value)
            .Select(trace => new
            {
                Trace = trace,
                Events = db
                    .AutomationTraceEvents.Where(value => value.TraceId == trace.Id)
                    .OrderBy(value => value.Sequence)
                    .ToArray(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            return new AutomationTraceReadOutcome.NotFound();
        }
        var trace = stored.Trace;
        return trace.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime
            ? new AutomationTraceReadOutcome.Expired()
            : new AutomationTraceReadOutcome.Available(
                new(
                    new(trace.Id),
                    trace.SchemaVersion,
                    trace.FlowId is { } flow ? new(flow) : null,
                    trace.ProductionRunId is { } run ? new(run) : null,
                    new(trace.CreatedAtUtc, TimeSpan.Zero),
                    new(trace.ExpiresAtUtc, TimeSpan.Zero),
                    trace.ByteCount,
                    (AutomationTraceTruncation)trace.Truncation,
                    [
                        .. stored.Events.Select(value => new AutomationTraceEntry(
                            value.Sequence,
                            JsonSerializer.Deserialize<AutomationTraceEventData>(
                                value.EventJson,
                                _json
                            )!
                        )),
                    ]
                )
            );
    }

    public async Task<ImmutableArray<AutomationTraceSummary>> ListAsync(
        AutomationHostId hostId,
        AutomationFlowId? flowId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var selectedFlow = flowId?.Value;
        var rows = await db
            .AutomationTraces.AsNoTracking()
            .Where(trace =>
                trace.HostId == hostId.Value
                && trace.ExpiresAtUtc > now
                && (selectedFlow == null || trace.FlowId == selectedFlow)
            )
            .OrderByDescending(trace => trace.CreatedAtUtc)
            .ThenByDescending(trace => trace.Id)
            .Take(QueryLimit)
            .ToArrayAsync(cancellationToken);
        return
        [
            .. rows.Select(trace => new AutomationTraceSummary(
                new(trace.Id),
                trace.FlowId is { } flow ? new(flow) : null,
                trace.ProductionRunId is { } run ? new(run) : null,
                new(trace.CreatedAtUtc, TimeSpan.Zero),
                new(trace.ExpiresAtUtc, TimeSpan.Zero),
                (AutomationTraceTruncation)trace.Truncation
            )),
        ];
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = db
            .AutomationTraces.Where(trace => trace.ExpiresAtUtc <= now)
            .OrderBy(trace => trace.ExpiresAtUtc)
            .ThenBy(trace => trace.Id)
            .Take(CleanupBatchSize)
            .Select(trace => trace.Id);
        return await db
            .AutomationTraces.Where(trace => ids.Contains(trace.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
