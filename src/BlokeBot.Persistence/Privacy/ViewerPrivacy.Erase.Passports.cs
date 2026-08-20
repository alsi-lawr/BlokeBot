using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ErasePassportsAsync(ErasureContext context)
    {
        var db = context.Db;
        var passportIds = context.PassportIds;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        await ViewerPassportAmbiguityTombstones.PersistForPassportsAsync(db, passportIds, ct);
        Record(
            context,
            "viewer-passports.logins",
            await db.ViewerPassportLogins.CountAsync(
                x => passportIds.Contains(x.PassportId) && (hostId == null || x.HostId == hostId),
                ct
            )
        );
        Record(
            context,
            "viewer-passports.stream-attendance",
            await db.ViewerPassportStreamAttendances.CountAsync(
                x => passportIds.Contains(x.PassportId) && (hostId == null || x.HostId == hostId),
                ct
            )
        );
        Record(
            context,
            "viewer-passports.profiles",
            await db.ViewerPassports.Where(x => passportIds.Contains(x.Id)).ExecuteDeleteAsync(ct)
        );
    }
}
