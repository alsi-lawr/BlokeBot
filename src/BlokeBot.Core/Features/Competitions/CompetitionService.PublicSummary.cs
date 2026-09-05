using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Competitions;

internal sealed record CompetitionSummaryItem(
    string Name,
    CompetitionStatus Status,
    DateTime? CompletedAtUtc
);

internal sealed record CompetitionPublicSummary(
    CompetitionSummaryItem? Current,
    IReadOnlyList<CompetitionSummaryItem> Completed
);

public sealed partial class CompetitionService
{
    internal async Task<CompetitionPublicSummary?> GetPublicSummaryAsync(
        int hostId,
        CancellationToken ct
    )
    {
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return null;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var visible = db
            .Competitions.AsNoTracking()
            .Where(value => value.HostId == hostId && value.Status != CompetitionStatus.Draft);
        var current = await visible
            .OrderByDescending(value =>
                value.Status == CompetitionStatus.Registration
                || value.Status == CompetitionStatus.Running
            )
            .ThenBy(value => value.Status == CompetitionStatus.Archived)
            .ThenByDescending(value => value.UpdatedAtUtc)
            .ThenBy(value => value.Id)
            .Select(value => new CompetitionSummaryItem(
                value.Name,
                value.Status,
                value.CompletedAtUtc
            ))
            .FirstOrDefaultAsync(ct);
        var completed = await visible
            .Where(value => value.CompletedAtUtc != null)
            .OrderByDescending(value => value.CompletedAtUtc)
            .ThenBy(value => value.Id)
            .Take(5)
            .Select(value => new CompetitionSummaryItem(
                value.Name,
                value.Status,
                value.CompletedAtUtc
            ))
            .ToArrayAsync(ct);
        return new(current, completed);
    }
}
