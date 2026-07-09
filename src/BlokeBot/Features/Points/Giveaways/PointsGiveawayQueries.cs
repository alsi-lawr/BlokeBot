using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Giveaways;

internal static class PointsGiveawayQueries
{
    public static async Task<PointsSettings> LoadSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db.PointsSettings.AsNoTracking().SingleOrDefaultAsync(x => x.HostId == hostId, ct)
        ?? new PointsSettings { HostId = hostId };

    public static async Task<ReplyDeliveryMap> LoadReplyDeliveryAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyDeliveryFeature.Points,
            ReplyDeliverySettingWriter.HostScopeId,
            ct
        );

    public static async Task<bool> HasActiveGiveawayAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db.PointsGiveaways.AnyAsync(
            x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active,
            ct
        );

    public static async Task<int?> FindActiveGiveawayIdAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .PointsGiveaways.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active)
            .OrderByDescending(x => x.StartedAtUtc)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);

    public static async Task<DateTime?> FindLastStartedAfterAsync(
        BlokeBotDbContext db,
        int hostId,
        DateTime startedAfterUtc,
        CancellationToken ct
    ) =>
        await db
            .PointsGiveaways.AsNoTracking()
            .Where(x => x.HostId == hostId && x.StartedAtUtc > startedAfterUtc)
            .OrderByDescending(x => x.StartedAtUtc)
            .Select(x => (DateTime?)x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);

    public static PointsGiveawayView ToView(PointsGiveaway giveaway) =>
        new(
            giveaway.Id,
            giveaway.Status,
            giveaway.StartedAtUtc,
            giveaway.EndsAtUtc,
            giveaway.Entrants.OrderBy(x => x.JoinedAtUtc).Select(x => x.Login).ToArray(),
            giveaway
                .Winners.Select(x => new PointsGiveawayWinnerView(
                    x.Login,
                    PointAmount.ParseAbsolute(x.Payout)
                ))
                .ToArray()
        );
}
