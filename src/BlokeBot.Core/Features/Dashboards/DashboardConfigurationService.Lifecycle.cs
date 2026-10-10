using BlokeBot.Core.Auth.Sessions;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed partial class DashboardConfigurationService
{
    internal async Task<DashboardSaveOutcome> RenameAsync(
        AuthenticatedSession session,
        DashboardHostId hostId,
        DashboardId id,
        DashboardRevision expectedRevision,
        string name,
        CancellationToken ct
    )
    {
        var current = await GetAsync(session, hostId, id, ct);
        var command = new SaveDashboardCommand(
            hostId,
            id,
            expectedRevision,
            name,
            current.Match(value => value.Value.Document, _ => DashboardDocument.Empty)
        );
        return await current.Match(
            _ => SaveAsync(session, command, ct),
            denied =>
                Task.FromResult<DashboardSaveOutcome>(
                    new DashboardSaveOutcome.Rejected(command, denied.Reason)
                )
        );
    }

    internal async Task<DashboardResult<DashboardId>> DeleteAsync(
        AuthenticatedSession session,
        DashboardHostId hostId,
        DashboardId id,
        DashboardRevision expectedRevision,
        CancellationToken ct
    )
    {
        var authorization = await authority.AuthorizeAsync(session, hostId, ct);
        if (authorization is DashboardResult<DashboardHostId>.Rejected denied)
        {
            return new DashboardResult<DashboardId>.Rejected(denied.Reason);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deleted = await db
            .ChannelDashboards.Where(row =>
                row.HostId == hostId.Value
                && row.Id == id.Value
                && row.Revision == expectedRevision.Value
            )
            .ExecuteDeleteAsync(ct);
        return deleted == 1
            ? new DashboardResult<DashboardId>.Succeeded(id)
            : new DashboardResult<DashboardId>.Rejected(
                await ReadAsync(db, hostId, id, ct) is null
                    ? DashboardRejection.Missing
                    : DashboardRejection.Conflict
            );
    }
}
