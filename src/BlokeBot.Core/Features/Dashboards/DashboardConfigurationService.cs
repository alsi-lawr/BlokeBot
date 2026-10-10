using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed partial class DashboardConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    DashboardAuthority authority
)
{
    internal async Task<DashboardResult<IReadOnlyList<DashboardSummary>>> ListAsync(
        AuthenticatedSession session,
        DashboardHostId hostId,
        CancellationToken ct
    )
    {
        var authorization = await authority.AuthorizeAsync(session, hostId, ct);
        if (authorization is DashboardResult<DashboardHostId>.Rejected denied)
        {
            return new DashboardResult<IReadOnlyList<DashboardSummary>>.Rejected(denied.Reason);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db
            .ChannelDashboards.AsNoTracking()
            .Where(row => row.HostId == hostId.Value)
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Select(row => new DashboardSummary(new(row.Id), row.Name, new(row.Revision)))
            .ToArrayAsync(ct);
        return new DashboardResult<IReadOnlyList<DashboardSummary>>.Succeeded(rows);
    }

    internal async Task<DashboardResult<DashboardSnapshot>> GetAsync(
        AuthenticatedSession session,
        DashboardHostId hostId,
        DashboardId id,
        CancellationToken ct
    )
    {
        var authorization = await authority.AuthorizeAsync(session, hostId, ct);
        if (authorization is DashboardResult<DashboardHostId>.Rejected denied)
        {
            return new DashboardResult<DashboardSnapshot>.Rejected(denied.Reason);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await ReadAsync(db, hostId, id, ct);
        return row is null
            ? new DashboardResult<DashboardSnapshot>.Rejected(DashboardRejection.Missing)
            : new DashboardResult<DashboardSnapshot>.Succeeded(Snapshot(row));
    }

    internal async Task<DashboardResult<DashboardSnapshot>> CreateAsync(
        AuthenticatedSession session,
        DashboardHostId hostId,
        string name,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new DashboardResult<DashboardSnapshot>.Rejected(DashboardRejection.Invalid);
        }
        var authorization = await authority.AuthorizeAsync(session, hostId, ct);
        if (authorization is DashboardResult<DashboardHostId>.Rejected denied)
        {
            return new DashboardResult<DashboardSnapshot>.Rejected(denied.Reason);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = new ChannelDashboard
        {
            Id = Guid.NewGuid(),
            HostId = hostId.Value,
            Name = name.Trim(),
            Revision = 1,
            DocumentJson = DashboardDocuments.Serialize(DashboardDocument.Empty),
        };
        _ = db.ChannelDashboards.Add(row);
        _ = await db.SaveChangesAsync(ct);
        return new DashboardResult<DashboardSnapshot>.Succeeded(Snapshot(row));
    }

    internal async Task<DashboardSaveOutcome> SaveAsync(
        AuthenticatedSession session,
        SaveDashboardCommand command,
        CancellationToken ct
    )
    {
        if (
            command.Id.Value == Guid.Empty
            || command.ExpectedRevision.Value <= 0
            || string.IsNullOrWhiteSpace(command.Name)
            || !DashboardDocuments.Valid(command.Document)
        )
        {
            return new DashboardSaveOutcome.Rejected(command, DashboardRejection.Invalid);
        }
        var authorization = await authority.AuthorizeAsync(session, command.HostId, ct);
        if (authorization is DashboardResult<DashboardHostId>.Rejected denied)
        {
            return new DashboardSaveOutcome.Rejected(command, denied.Reason);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var json = DashboardDocuments.Serialize(command.Document);
        var name = command.Name.Trim();
        var updated = await db
            .ChannelDashboards.Where(row =>
                row.HostId == command.HostId.Value
                && row.Id == command.Id.Value
                && row.Revision == command.ExpectedRevision.Value
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(row => row.Name, name)
                        .SetProperty(row => row.DocumentJson, json)
                        .SetProperty(row => row.Revision, row => row.Revision + 1),
                ct
            );
        if (updated == 1)
        {
            await transaction.CommitAsync(ct);
            return new DashboardSaveOutcome.Saved(
                new(command.Id, name, new(command.ExpectedRevision.Value + 1), command.Document)
            );
        }
        var current = await ReadAsync(db, command.HostId, command.Id, ct);
        return current is null
            ? new DashboardSaveOutcome.Rejected(command, DashboardRejection.Missing)
            : new DashboardSaveOutcome.Conflict(command, Snapshot(current));
    }

    private static Task<ChannelDashboard?> ReadAsync(
        BlokeBotDbContext db,
        DashboardHostId hostId,
        DashboardId id,
        CancellationToken ct
    ) =>
        db
            .ChannelDashboards.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostId == hostId.Value && row.Id == id.Value, ct);

    private static DashboardSnapshot Snapshot(ChannelDashboard row) =>
        new(
            new(row.Id),
            row.Name,
            new(row.Revision),
            DashboardDocuments.Deserialize(row.DocumentJson)
        );
}
