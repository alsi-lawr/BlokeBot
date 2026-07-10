using BlokeBot.Eventing;
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
    public async Task<DurableAlert> CreateAsync(
        int hostId,
        DurableAlertSeverity severity,
        string source,
        string sourceKey,
        string title,
        string message,
        string? linkPath,
        CancellationToken ct
    )
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
            return existing;

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
        await events.PublishAsync(AppEventKind.AlertsChanged);
        return alert;
    }

    public async Task<bool> AcknowledgeAsync(
        int hostId,
        int alertId,
        string actorLogin,
        CancellationToken ct
    )
    {
        var actor = NormalizeRequired(actorLogin, nameof(actorLogin));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alert = await db.DurableAlerts.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.Id == alertId,
            ct
        );
        if (alert is null)
            return false;

        if (alert.AcknowledgedAtUtc is not null)
            return true;

        alert.AcknowledgedAtUtc = UtcNow();
        alert.AcknowledgedByLogin = actor;
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.AlertsChanged);
        return true;
    }

    public async Task<int> CountActiveAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.DurableAlerts.CountAsync(
            x => x.HostId == hostId && x.AcknowledgedAtUtc == null,
            ct
        );
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);

        return value.Trim();
    }
}
