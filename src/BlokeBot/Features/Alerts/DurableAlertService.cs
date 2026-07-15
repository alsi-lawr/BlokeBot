using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Alerts;

public sealed class DurableAlertService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider timeProvider,
    EventBus<AppEventKind> events
)
{
    public IO<DurableAlertCreateOutcome, Never> Create(
        int hostId,
        DurableAlertSeverity severity,
        string source,
        string sourceKey,
        string title,
        string message,
        string? linkPath
    )
    {
        return IO<DurableAlertCreateOutcome, Never>.Create(async ct =>
        {
            var normalizedSource = NormalizeRequired(source, nameof(source));
            var normalizedSourceKey = NormalizeRequired(sourceKey, nameof(sourceKey));

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.DurableAlerts.SingleOrDefaultAsync(
                x =>
                    x.HostId == hostId
                    && x.Source == normalizedSource
                    && x.SourceKey == normalizedSourceKey
                    && x.AcknowledgedAtUtc == null,
                ct
            );
            if (existing is not null)
            {
                return Result<DurableAlertCreateOutcome, Never>.Success(
                    new DurableAlertCreateOutcome.Existing(existing)
                );
            }

            var alert = new DurableAlert
            {
                HostId = hostId,
                Severity = severity,
                Source = normalizedSource,
                SourceKey = normalizedSourceKey,
                Title = NormalizeRequired(title, nameof(title)),
                Message = NormalizeRequired(message, nameof(message)),
                LinkPath = string.IsNullOrWhiteSpace(linkPath) ? null : linkPath.Trim(),
                CreatedAtUtc = UtcNow(),
            };
            db.DurableAlerts.Add(alert);
            await db.SaveChangesAsync(ct);
            await events.PublishAsync(AppEventKind.AlertsChanged, ct);
            return Result<DurableAlertCreateOutcome, Never>.Success(
                new DurableAlertCreateOutcome.Created(alert)
            );
        });
    }

    public IO<DurableAlertAcknowledgement, Never> Acknowledge(
        int hostId,
        int alertId,
        string actorLogin
    )
    {
        return IO<DurableAlertAcknowledgement, Never>.Create(async ct =>
        {
            var actor = NormalizeRequired(actorLogin, nameof(actorLogin));
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var alert = await db.DurableAlerts.SingleOrDefaultAsync(
                x => x.HostId == hostId && x.Id == alertId,
                ct
            );
            if (alert is null)
            {
                return Result<DurableAlertAcknowledgement, Never>.Success(
                    new DurableAlertAcknowledgement.NotFound()
                );
            }

            if (alert.AcknowledgedAtUtc is not null)
            {
                return Result<DurableAlertAcknowledgement, Never>.Success(
                    new DurableAlertAcknowledgement.AlreadyAcknowledged()
                );
            }

            alert.AcknowledgedAtUtc = UtcNow();
            alert.AcknowledgedByLogin = actor;
            await db.SaveChangesAsync(ct);
            await events.PublishAsync(AppEventKind.AlertsChanged, ct);
            return Result<DurableAlertAcknowledgement, Never>.Success(
                new DurableAlertAcknowledgement.Acknowledged()
            );
        });
    }

    public async Task<int> CountActiveAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.DurableAlerts.CountAsync(
            x => x.HostId == hostId && x.AcknowledgedAtUtc == null,
            ct
        );
    }

    public async Task<DurableAlertState> LoadStateAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alerts = await db
            .DurableAlerts.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new DurableAlertItem(
                x.Id,
                x.Severity,
                x.Source,
                x.SourceKey,
                x.Title,
                x.Message,
                x.LinkPath,
                x.CreatedAtUtc,
                x.AcknowledgedAtUtc,
                x.AcknowledgedByLogin
            ))
            .ToArrayAsync(ct);

        return new DurableAlertState(
            alerts.Where(x => x.IsActive).ToArray(),
            alerts.Where(x => !x.IsActive).ToArray()
        );
    }

    private DateTime UtcNow()
    {
        return timeProvider.GetUtcNow().UtcDateTime;
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }
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
    DateTime? AcknowledgedAtUtc,
    string? AcknowledgedByLogin
)
{
    public bool IsActive => AcknowledgedAtUtc is null;
}
