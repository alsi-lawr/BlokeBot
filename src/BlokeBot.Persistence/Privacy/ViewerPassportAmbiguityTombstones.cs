using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static class ViewerPassportAmbiguityTombstones
{
    public static async Task PersistForPassportsAsync(
        BlokeBotDbContext db,
        IReadOnlyCollection<long> passportIds,
        CancellationToken cancellationToken
    )
    {
        if (passportIds.Count == 0)
        {
            return;
        }
        var aliases = await db
            .ViewerPassportLogins.Where(value => passportIds.Contains(value.PassportId))
            .Select(value => new TombstoneCandidate(value.HostId, value.Login, value.LastSeenAtUtc))
            .ToArrayAsync(cancellationToken);
        var currentLogins = await db
            .ViewerPassports.Where(value =>
                passportIds.Contains(value.Id) && value.Login != string.Empty
            )
            .Select(value => new TombstoneCandidate(value.HostId, value.Login, value.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);
        var candidates = aliases
            .Concat(currentLogins)
            .GroupBy(value => new { value.HostId, value.Login })
            .Select(values => new TombstoneCandidate(
                values.Key.HostId,
                values.Key.Login,
                values.Max(value => value.DetectedAtUtc)
            ))
            .ToArray();
        foreach (var candidate in candidates)
        {
            _ = await MainDatabaseStatements.TryRecordViewerPassportAmbiguityAsync(
                db,
                candidate.HostId,
                candidate.Login,
                candidate.DetectedAtUtc,
                cancellationToken
            );
        }
    }

    private sealed record TombstoneCandidate(int HostId, string Login, DateTime DetectedAtUtc);
}
