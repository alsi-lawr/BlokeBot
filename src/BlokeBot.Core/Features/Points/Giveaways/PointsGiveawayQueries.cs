using System.Collections.Immutable;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.Giveaways;

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
            ReplyFeature.Points,
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

    public static IO<Option<int>, Never> FindActiveGiveawayId(BlokeBotDbContext db, int hostId) =>
        IO<Option<int>, Never>.Create(async ct =>
        {
            var giveawayId = await db
                .PointsGiveaways.AsNoTracking()
                .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active)
                .OrderByDescending(x => x.StartedAtUtc)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
            return Result<Option<int>, Never>.Success(
                giveawayId.HasValue ? Option<int>.Some(giveawayId.Value) : Option<int>.None
            );
        });

    public static IO<Option<DateTime>, Never> FindLastStartedAfter(
        BlokeBotDbContext db,
        int hostId,
        DateTime startedAfterUtc
    ) =>
        IO<Option<DateTime>, Never>.Create(async ct =>
        {
            var startedAt = await db
                .PointsGiveaways.AsNoTracking()
                .Where(x => x.HostId == hostId && x.StartedAtUtc > startedAfterUtc)
                .OrderByDescending(x => x.StartedAtUtc)
                .Select(x => (DateTime?)x.StartedAtUtc)
                .FirstOrDefaultAsync(ct);
            return Result<Option<DateTime>, Never>.Success(
                startedAt.HasValue ? Option<DateTime>.Some(startedAt.Value) : Option<DateTime>.None
            );
        });

    public static async Task<PointsGiveawayView?> LoadActiveViewAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var giveaway = await db
            .PointsGiveaways.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active)
            .OrderByDescending(x => x.StartedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.StartedAtUtc,
                x.EndsAtUtc,
                x.CompletedAtUtc,
            })
            .FirstOrDefaultAsync(ct);
        if (giveaway is null)
        {
            return null;
        }

        var entrants = await db
            .PointsGiveawayEntrants.AsNoTracking()
            .Where(x => x.GiveawayId == giveaway.Id)
            .OrderBy(x => x.JoinedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => x.Login)
            .ToArrayAsync(ct);
        var winners = await db
            .PointsGiveawayWinners.AsNoTracking()
            .Where(x => x.GiveawayId == giveaway.Id)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Login, x.Payout })
            .ToArrayAsync(ct);

        return new(
            giveaway.Id,
            PointsGiveawayLifecycle.FromPersistence(
                giveaway.Status,
                giveaway.StartedAtUtc,
                giveaway.CompletedAtUtc
            ),
            giveaway.StartedAtUtc,
            giveaway.EndsAtUtc,
            entrants.ToImmutableArray(),
            winners
                .Select(x => new PointsGiveawayWinnerView(
                    x.Login,
                    PointAmount.ParseAbsolute(x.Payout)
                ))
                .ToImmutableArray()
        );
    }
}
