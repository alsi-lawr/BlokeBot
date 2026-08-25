using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Alerts;

public sealed partial class DurableAlertService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider timeProvider,
    EventBus<AppEventKind> events
) : IDisposable
{
    private readonly SemaphoreSlim _reportGate = new(1, 1);

    public IO<DurableAlertCreateOutcome, Never> Create(
        int hostId,
        DurableAlertSeverity severity,
        string source,
        string sourceKey,
        string title,
        string message,
        string? linkPath
    ) =>
        IO<DurableAlertCreateOutcome, Never>.Create(async ct =>
        {
            var report = Report(
                hostId,
                severity,
                source,
                sourceKey,
                title,
                message,
                linkPath,
                UtcNow()
            );
            await using var operation = await BeginReportOperationAsync(ct);
            report = report with { OccurredAtUtc = UtcNow() };
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var change = await operation.StageAsync(db, report, ct);
            _ = await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await operation.PublishCommittedAsync(change);
            return Result<DurableAlertCreateOutcome, Never>.Success(
                change.WasCreated
                    ? new DurableAlertCreateOutcome.Created(change.Alert)
                    : new DurableAlertCreateOutcome.Existing(change.Alert)
            );
        });

    public IO<DurableAlertAcknowledgement, Never> Acknowledge(
        int hostId,
        int alertId,
        string actorLogin
    ) =>
        IO<DurableAlertAcknowledgement, Never>.Create(async ct =>
        {
            var actor = NormalizeRequired(actorLogin, nameof(actorLogin));
            await using var operation = await BeginReportOperationAsync(ct);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var acknowledgedAtUtc = UtcNow();
            var changed = await db
                .DurableAlerts.Where(alert =>
                    alert.HostId == hostId && alert.Id == alertId && alert.AcknowledgedAtUtc == null
                )
                .ExecuteUpdateAsync(
                    update =>
                        update
                            .SetProperty(alert => alert.AcknowledgedAtUtc, acknowledgedAtUtc)
                            .SetProperty(alert => alert.AcknowledgedByLogin, actor),
                    ct
                );
            if (changed == 1)
            {
                await operation.PublishCommittedAsync();
                return Result<DurableAlertAcknowledgement, Never>.Success(
                    new DurableAlertAcknowledgement.Acknowledged()
                );
            }

            var exists = await db.DurableAlerts.AnyAsync(
                alert => alert.HostId == hostId && alert.Id == alertId,
                ct
            );
            return Result<DurableAlertAcknowledgement, Never>.Success(
                exists
                    ? new DurableAlertAcknowledgement.AlreadyAcknowledged()
                    : new DurableAlertAcknowledgement.NotFound()
            );
        });

    internal IO<DurableAlertResolution, Never> Resolve(
        int hostId,
        string source,
        string sourceKey,
        string actorLogin
    ) =>
        IO<DurableAlertResolution, Never>.Create(async ct =>
        {
            var identity = new DurableAlertIdentity(
                hostId,
                NormalizeRequired(source, nameof(source)),
                NormalizeRequired(sourceKey, nameof(sourceKey))
            );
            var actor = NormalizeRequired(actorLogin, nameof(actorLogin));
            await using var operation = await BeginReportOperationAsync(ct);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var change = await operation.StageResolutionAsync(db, identity, actor, UtcNow(), ct);
            await transaction.CommitAsync(ct);
            await operation.PublishCommittedAsync(change);
            return Result<DurableAlertResolution, Never>.Success(
                change.WasResolved
                    ? new DurableAlertResolution.Resolved()
                    : new DurableAlertResolution.NotActive()
            );
        });

    public async Task<int> CountActiveAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.DurableAlerts.CountAsync(
            alert => alert.HostId == hostId && alert.AcknowledgedAtUtc == null,
            ct
        );
    }

    public async Task<DurableAlertState> LoadStateAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alerts = await db
            .DurableAlerts.AsNoTracking()
            .Where(alert => alert.HostId == hostId)
            .OrderByDescending(alert => alert.LastOccurredAtUtc)
            .Select(alert => new DurableAlertItem(
                alert.Id,
                alert.Severity,
                alert.Source,
                alert.SourceKey,
                alert.Title,
                alert.Message,
                alert.LinkPath,
                alert.CreatedAtUtc,
                alert.OccurrenceCount,
                alert.LastOccurredAtUtc,
                alert.AcknowledgedAtUtc,
                alert.AcknowledgedByLogin
            ))
            .ToArrayAsync(ct);

        return new DurableAlertState(
            alerts.Where(static alert => alert.IsActive).ToArray(),
            alerts.Where(static alert => !alert.IsActive).ToArray()
        );
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    public void Dispose() => _reportGate.Dispose();
}

public abstract record DurableAlertCreateOutcome
{
    private DurableAlertCreateOutcome() { }

    public abstract DurableAlert Alert { get; init; }

    public sealed record Created(DurableAlert Alert) : DurableAlertCreateOutcome;

    public sealed record Existing(DurableAlert Alert) : DurableAlertCreateOutcome;
}

public abstract record DurableAlertAcknowledgement
{
    private DurableAlertAcknowledgement() { }

    public sealed record NotFound : DurableAlertAcknowledgement;

    public sealed record AlreadyAcknowledged : DurableAlertAcknowledgement;

    public sealed record Acknowledged : DurableAlertAcknowledgement;
}

internal abstract record DurableAlertResolution
{
    private DurableAlertResolution() { }

    public sealed record NotActive : DurableAlertResolution;

    public sealed record Resolved : DurableAlertResolution;
}

public sealed record DurableAlertState(
    IReadOnlyList<DurableAlertItem> Active,
    IReadOnlyList<DurableAlertItem> History
)
{
    public int ActiveCount => Active.Count;
}

public sealed record DurableAlertItem(
    int Id,
    DurableAlertSeverity Severity,
    string Source,
    string SourceKey,
    string Title,
    string Message,
    string? LinkPath,
    DateTime CreatedAtUtc,
    int OccurrenceCount,
    DateTime LastOccurredAtUtc,
    DateTime? AcknowledgedAtUtc,
    string? AcknowledgedByLogin
)
{
    public bool IsActive => AcknowledgedAtUtc is null;
}
