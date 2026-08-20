using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task<ViewerDataExport> ExportInSnapshotAsync(
        BlokeBotDbContext db,
        PrivacySubject subject,
        int? hostId,
        CancellationToken ct
    )
    {
        var sections = new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal);
        var scope = await ResolveIdentityScopeAsync(db, subject, hostId, ct);
        var userId = scope.UserId;
        var passportIds = scope.PassportIds;
        var safeLoginClaims = SafeLoginClaims(db, passportIds);
        var safeGlobalLoginClaims = SafeGlobalLoginClaims(
            db,
            safeLoginClaims,
            scope.GlobalAliasOwnerUserId
        );
        var linkedLedgerClaims = await ResolveLinkedLedgerClaimsAsync(
            db,
            userId,
            safeLoginClaims,
            hostId,
            ct
        );
        var context = new ExportContext(
            db,
            sections,
            userId,
            scope.IdIdentityKey,
            passportIds,
            safeLoginClaims,
            safeGlobalLoginClaims,
            linkedLedgerClaims,
            hostId,
            ct
        );

        await ExportAccountsAndPointsAsync(context);
        await ExportCommandsAndAccessAsync(context);
        await ExportMediaAndRequestsAsync(context);
        await ExportBountiesAndCompetitionsAsync(context);
        await ExportBingoAndCommunityAsync(context);
        await ExportPassportsAndQueuesAsync(context);
        await ExportMomentsAndOverlaysAsync(context);

        return new ViewerDataExport(sections);
    }

    private static async Task AddExportSectionAsync<T>(
        ExportContext context,
        string section,
        IQueryable<T> query
    )
        where T : class
    {
        var rows = await query.AsNoTracking().ToListAsync(context.CancellationToken);
        if (rows.Count > 0)
        {
            context.Sections[section] = rows;
        }
    }

    private sealed record ExportContext(
        BlokeBotDbContext Db,
        Dictionary<string, IReadOnlyList<object>> Sections,
        string UserId,
        string IdKey,
        long[] PassportIds,
        IQueryable<ViewerPassportLogin> SafeLoginClaims,
        IQueryable<ViewerPassportLogin> SafeGlobalLoginClaims,
        LinkedLedgerClaims LinkedLedgerClaims,
        int? HostId,
        CancellationToken CancellationToken
    );
}
