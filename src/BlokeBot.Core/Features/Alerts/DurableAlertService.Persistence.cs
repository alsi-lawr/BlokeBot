using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Alerts;

public sealed partial class DurableAlertService
{
    internal async ValueTask<ReportOperation> BeginReportOperationAsync(
        CancellationToken cancellationToken
    )
    {
        await _reportGate.WaitAsync(cancellationToken);
        return new ReportOperation(this);
    }

    private async Task<DurableAlertPendingChange> StageReportAsync(
        BlokeBotDbContext db,
        DurableAlertReport report,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(report);

        var normalized = Report(
            report.Identity.HostId,
            report.Severity,
            report.Identity.Source,
            report.Identity.SourceKey,
            report.Title,
            report.Message,
            report.LinkPath,
            report.OccurredAtUtc
        );
        var tracked = db.DurableAlerts.Local.SingleOrDefault(alert =>
            IsActiveIssue(alert, normalized.Identity)
        );
        if (tracked is not null)
        {
            ApplyRecurrence(tracked, normalized);
            return new DurableAlertPendingChange(tracked, wasCreated: false);
        }

        if (db.Database.CurrentTransaction is not null)
        {
            var changed = await db
                .DurableAlerts.Where(alert =>
                    alert.HostId == normalized.Identity.HostId
                    && alert.Source == normalized.Identity.Source
                    && alert.SourceKey == normalized.Identity.SourceKey
                    && alert.AcknowledgedAtUtc == null
                )
                .ExecuteUpdateAsync(
                    update =>
                        update
                            .SetProperty(
                                alert => alert.OccurrenceCount,
                                alert => alert.OccurrenceCount + 1
                            )
                            .SetProperty(alert => alert.LastOccurredAtUtc, normalized.OccurredAtUtc)
                            .SetProperty(alert => alert.Severity, normalized.Severity)
                            .SetProperty(alert => alert.Title, normalized.Title)
                            .SetProperty(alert => alert.Message, normalized.Message)
                            .SetProperty(alert => alert.LinkPath, normalized.LinkPath),
                    cancellationToken
                );
            if (changed == 1)
            {
                var recurrent = await ActiveIssue(db, normalized.Identity)
                    .SingleAsync(cancellationToken);
                return new DurableAlertPendingChange(recurrent, wasCreated: false);
            }
        }
        else
        {
            var existing = await ActiveIssue(db, normalized.Identity)
                .SingleOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                ApplyRecurrence(existing, normalized);
                return new DurableAlertPendingChange(existing, wasCreated: false);
            }
        }

        var created = new DurableAlert
        {
            HostId = normalized.Identity.HostId,
            Severity = normalized.Severity,
            Source = normalized.Identity.Source,
            SourceKey = normalized.Identity.SourceKey,
            Title = normalized.Title,
            Message = normalized.Message,
            LinkPath = normalized.LinkPath,
            CreatedAtUtc = normalized.OccurredAtUtc,
            OccurrenceCount = 1,
            LastOccurredAtUtc = normalized.OccurredAtUtc,
        };
        _ = db.DurableAlerts.Add(created);
        return new DurableAlertPendingChange(created, wasCreated: true);
    }

    private async ValueTask PublishCommittedAsync(DurableAlertPendingChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        change.ClaimPublication();
        await PublishCommittedAsync();
    }

    private async ValueTask PublishCommittedAsync() =>
        _ = await events.PublishAsync(AppEventKind.AlertsChanged, CancellationToken.None);

    private void EndReportOperation() => _ = _reportGate.Release();

    private static IQueryable<DurableAlert> ActiveIssue(
        BlokeBotDbContext db,
        DurableAlertIdentity identity
    ) =>
        db.DurableAlerts.Where(alert =>
            alert.HostId == identity.HostId
            && alert.Source == identity.Source
            && alert.SourceKey == identity.SourceKey
            && alert.AcknowledgedAtUtc == null
        );

    private static bool IsActiveIssue(DurableAlert alert, DurableAlertIdentity identity) =>
        alert.HostId == identity.HostId
        && alert.Source == identity.Source
        && alert.SourceKey == identity.SourceKey
        && alert.AcknowledgedAtUtc is null;

    private static void ApplyRecurrence(DurableAlert alert, DurableAlertReport report)
    {
        alert.OccurrenceCount++;
        alert.LastOccurredAtUtc = report.OccurredAtUtc;
        alert.Severity = report.Severity;
        alert.Title = report.Title;
        alert.Message = report.Message;
        alert.LinkPath = report.LinkPath;
    }

    private static DurableAlertReport Report(
        int hostId,
        DurableAlertSeverity severity,
        string source,
        string sourceKey,
        string title,
        string message,
        string? linkPath,
        DateTime occurredAtUtc
    ) =>
        new(
            new DurableAlertIdentity(
                hostId,
                NormalizeRequired(source, nameof(source)),
                NormalizeRequired(sourceKey, nameof(sourceKey))
            ),
            severity,
            NormalizeRequired(title, nameof(title)),
            NormalizeRequired(message, nameof(message)),
            string.IsNullOrWhiteSpace(linkPath) ? null : linkPath.Trim(),
            occurredAtUtc
        );

    private static string NormalizeRequired(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();

    internal sealed class ReportOperation(DurableAlertService authority) : IAsyncDisposable
    {
        private DurableAlertService? _authority = authority;

        internal Task<DurableAlertPendingChange> StageAsync(
            BlokeBotDbContext db,
            DurableAlertReport report,
            CancellationToken cancellationToken
        ) => GetAuthority().StageReportAsync(db, report, cancellationToken);

        internal ValueTask PublishCommittedAsync(DurableAlertPendingChange change) =>
            GetAuthority().PublishCommittedAsync(change);

        internal ValueTask PublishCommittedAsync() => GetAuthority().PublishCommittedAsync();

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _authority, null)?.EndReportOperation();
            return ValueTask.CompletedTask;
        }

        private DurableAlertService GetAuthority() =>
            Volatile.Read(ref _authority)
            ?? throw new ObjectDisposedException(nameof(ReportOperation));
    }
}

internal sealed record DurableAlertIdentity(int HostId, string Source, string SourceKey);

internal sealed record DurableAlertReport(
    DurableAlertIdentity Identity,
    DurableAlertSeverity Severity,
    string Title,
    string Message,
    string? LinkPath,
    DateTime OccurredAtUtc
);

internal sealed class DurableAlertPendingChange(DurableAlert alert, bool wasCreated)
{
    private int _publicationClaimed;

    internal DurableAlert Alert { get; } = alert;

    internal bool WasCreated { get; } = wasCreated;

    internal void ClaimPublication()
    {
        if (Interlocked.Exchange(ref _publicationClaimed, 1) != 0)
        {
            throw new InvalidOperationException(
                "The committed alert change was already published."
            );
        }
    }
}
