using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Text;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Giveaways;

public sealed class PointsGiveawayService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointBalanceService balances,
    HostBotStatusService botStatus,
    IPointsRandom random,
    IPointsGiveawayScheduler scheduler,
    PointsChangeNotifier changes
)
{
    public async Task<PointsGiveawayView?> GetActiveGiveawayAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .PointsGiveaways.AsNoTracking()
            .Include(x => x.Entrants)
            .Include(x => x.Winners)
            .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active)
            .OrderByDescending(x => x.StartedAtUtc)
            .Select(x => ToView(x))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PointOperationResult> StartAsync(
        int hostId,
        string hostLogin,
        Func<string, CancellationToken, ValueTask>? reply,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await LoadSettingsAsync(db, hostId, ct);
        if (await HasActiveGiveawayAsync(db, hostId, ct))
            return Reply(false, settings.GiveawayAlreadyActiveReply, settings);

        var now = DateTime.UtcNow;
        var cooldownStart = now.AddSeconds(-settings.GiveawayCooldownSeconds);
        var lastStarted = await db
            .PointsGiveaways.AsNoTracking()
            .Where(x => x.HostId == hostId && x.StartedAtUtc > cooldownStart)
            .OrderByDescending(x => x.StartedAtUtc)
            .Select(x => (DateTime?)x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (lastStarted is not null)
        {
            var readyAt = lastStarted.Value.AddSeconds(settings.GiveawayCooldownSeconds);
            return Reply(
                false,
                Format(
                    settings.GiveawayCooldownReply,
                    settings,
                    timeLeft: FormatTimeLeft(readyAt - now)
                ),
                settings
            );
        }

        bool live;
        try
        {
            live = await botStatus.IsStreamLiveAsync(hostLogin, ct);
        }
        catch
        {
            live = false;
        }

        if (!live)
            return Reply(false, settings.StreamOfflineReply, settings);

        if (settings.GiveawayEligibility == PointsEligibilityMode.Followers)
        {
            var status = await botStatus.GetStatusAsync(hostLogin, ct);
            if (status.ModeratorState != HostBotModeratorState.IsModerator)
                return Reply(false, settings.FollowerEligibilityUnavailableReply, settings);
        }

        var giveaway = new PointsGiveaway
        {
            HostId = hostId,
            Status = PointsGiveawayStatus.Active,
            StartedAtUtc = now,
            EndsAtUtc = now.AddSeconds(Math.Max(1, settings.GiveawayDurationSeconds)),
            MinimumPayout = settings.GiveawayMinimumPayout,
            MaximumPayout = settings.GiveawayMaximumPayout,
            WinnerCount = Math.Max(1, settings.GiveawayWinnerCount),
            Eligibility = settings.GiveawayEligibility,
        };
        db.PointsGiveaways.Add(giveaway);
        await db.SaveChangesAsync(ct);
        scheduler.Schedule(
            new PointsGiveawaySchedule(
                giveaway.Id,
                hostId,
                hostLogin,
                giveaway.StartedAtUtc,
                giveaway.EndsAtUtc,
                reply
            )
        );
        await changes.NotifyChangedAsync();
        return Reply(true, settings.GiveawayStartedReply, settings);
    }

    public async Task<PointOperationResult> JoinAsync(
        int hostId,
        string hostLogin,
        string login,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await LoadSettingsAsync(db, hostId, ct);
        var giveaway = await db
            .PointsGiveaways.Include(x => x.Entrants)
            .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (giveaway is null)
            return Reply(false, settings.GiveawayNotActiveReply, settings);

        var normalized = LoginName.Parse(login).Value;
        if (giveaway.Entrants.Any(x => x.Login == normalized))
            return Reply(
                false,
                Format(settings.GiveawayAlreadyJoinedReply, settings, user: normalized),
                settings
            );

        var eligibility = await CheckEligibilityAsync(settings, hostLogin, normalized, tags, ct);
        if (eligibility == FollowerCheckResult.Unavailable)
            return Reply(false, settings.FollowerEligibilityUnavailableReply, settings);

        if (eligibility == FollowerCheckResult.NotEligible)
            return Reply(
                false,
                Format(settings.NotEligibleReply, settings, user: normalized),
                settings
            );

        db.PointsGiveawayEntrants.Add(
            new PointsGiveawayEntrant
            {
                GiveawayId = giveaway.Id,
                Login = normalized,
                JoinedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        return Reply(
            true,
            Format(settings.GiveawayJoinedReply, settings, user: normalized),
            settings
        );
    }

    public async Task<PointOperationResult> EndAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    )
    {
        return await DrawActiveAsync(hostId, hostLogin, ct);
    }

    public async Task<PointOperationResult> CancelAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await LoadSettingsAsync(db, hostId, ct);
        var giveaway = await db
            .PointsGiveaways.Where(x =>
                x.HostId == hostId && x.Status == PointsGiveawayStatus.Active
            )
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (giveaway is null)
            return Reply(false, settings.GiveawayNotActiveReply, settings);

        giveaway.Status = PointsGiveawayStatus.Cancelled;
        giveaway.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        scheduler.Cancel(giveaway.Id);
        await changes.NotifyChangedAsync();
        return Reply(true, settings.GiveawayCancelledReply, settings);
    }

    private async Task<PointOperationResult> DrawActiveAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await LoadSettingsAsync(db, hostId, ct);
        var giveawayId = await db
            .PointsGiveaways.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active)
            .OrderByDescending(x => x.StartedAtUtc)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (giveawayId is null)
            return Reply(false, settings.GiveawayNotActiveReply, settings);

        var result = await DrawAsync(giveawayId.Value, ct);
        if (result.Success)
            scheduler.Cancel(giveawayId.Value);

        return result;
    }

    internal async Task<PointOperationResult> DrawAsync(int giveawayId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var giveawayHeader = await db
            .PointsGiveaways.AsNoTracking()
            .Where(x => x.Id == giveawayId)
            .Select(x => new { x.HostId, x.Status })
            .SingleOrDefaultAsync(ct);
        if (giveawayHeader is null)
            return new PointOperationResult(false, string.Empty);

        var settings = await LoadSettingsAsync(db, giveawayHeader.HostId, ct);
        if (giveawayHeader.Status != PointsGiveawayStatus.Active)
            return Reply(false, settings.GiveawayNotActiveReply, settings);

        var now = DateTime.UtcNow;
        var claimed = await db
            .PointsGiveaways.Where(x =>
                x.Id == giveawayId && x.Status == PointsGiveawayStatus.Active
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(x => x.Status, PointsGiveawayStatus.Completed)
                        .SetProperty(x => x.CompletedAtUtc, now),
                ct
            );
        if (claimed == 0)
            return Reply(false, settings.GiveawayNotActiveReply, settings);

        var giveaway = await db
            .PointsGiveaways.Include(x => x.Entrants)
            .Include(x => x.Winners)
            .SingleAsync(x => x.Id == giveawayId, ct);
        var entrants = giveaway
            .Entrants.Select(x => x.Login)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (entrants.Count == 0)
        {
            await tx.CommitAsync(ct);
            await changes.NotifyChangedAsync();
            return Reply(true, settings.GiveawayNoEntrantsReply, settings);
        }

        var winnerCount = Math.Min(Math.Max(1, giveaway.WinnerCount), entrants.Count);
        var winners = entrants
            .OrderBy(_ => random.Next(0, int.MaxValue))
            .Take(winnerCount)
            .ToArray();
        foreach (var winner in winners)
        {
            var payout = RandomPayout(giveaway.MinimumPayout, giveaway.MaximumPayout);
            giveaway.Winners.Add(
                new PointsGiveawayWinner
                {
                    GiveawayId = giveaway.Id,
                    Login = winner,
                    Payout = payout.ToString(),
                }
            );
            await balances.AwardGiveawayAsync(
                db,
                giveaway.HostId,
                giveaway.Id,
                winner,
                payout,
                now,
                ct
            );
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await changes.NotifyChangedAsync();
        var winnerText = string.Join(
            ", ",
            giveaway.Winners.Select(x =>
                $"{x.Login} ({PointAmount.ParseAbsolute(x.Payout).ToDisplayString()})"
            )
        );
        return Reply(
            true,
            Format(settings.GiveawayEndedReply, settings, winners: winnerText),
            settings
        );
    }

    internal async Task<bool> ExpireAsync(int giveawayId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var expired = await db
            .PointsGiveaways.Where(x =>
                x.Id == giveawayId && x.Status == PointsGiveawayStatus.Active
            )
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(x => x.Status, PointsGiveawayStatus.Expired)
                        .SetProperty(x => x.CompletedAtUtc, DateTime.UtcNow),
                ct
            );

        if (expired == 0)
            return false;

        await changes.NotifyChangedAsync();
        return true;
    }

    internal async Task<string?> BuildUpdateMessageAsync(
        int giveawayId,
        DateTime endsAtUtc,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var giveaway = await db
            .PointsGiveaways.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == giveawayId, ct);
        if (giveaway is null || giveaway.Status != PointsGiveawayStatus.Active)
            return null;

        var settings = await LoadSettingsAsync(db, giveaway.HostId, ct);
        var message = Format(
            settings.GiveawayUpdateReply,
            settings,
            timeLeft: FormatTimeLeft(endsAtUtc - DateTime.UtcNow)
        );
        return message;
    }

    private async Task<FollowerCheckResult> CheckEligibilityAsync(
        PointsSettings settings,
        string hostLogin,
        string login,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        return settings.GiveawayEligibility switch
        {
            PointsEligibilityMode.Subscribers =>
                HasSubscriberBadge(tags)
                    ? FollowerCheckResult.Eligible
                    : FollowerCheckResult.NotEligible,
            PointsEligibilityMode.Followers => await botStatus.IsFollowerAsync(hostLogin, login, ct),
            _ => FollowerCheckResult.Eligible,
        };
    }

    private static string Format(
        string template,
        PointsSettings settings,
        string? user = null,
        string? winners = null,
        string? timeLeft = null
    ) =>
        TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["label"] = settings.PointLabel,
                ["user"] = user ?? string.Empty,
                ["winners"] = winners ?? string.Empty,
                ["time_left"] = timeLeft ?? string.Empty,
            }
        );

    private static string FormatTimeLeft(TimeSpan timeLeft)
    {
        var seconds = Math.Max(0, (int)Math.Round(timeLeft.TotalSeconds));
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }

    private static bool HasSubscriberBadge(IReadOnlyDictionary<string, string> tags)
    {
        if (!tags.TryGetValue("badges", out var badges))
            return false;

        return badges
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x.StartsWith("subscriber/", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> HasActiveGiveawayAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db.PointsGiveaways.AnyAsync(
            x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active,
            ct
        );

    private static async Task<PointsSettings> LoadSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db.PointsSettings.AsNoTracking().SingleOrDefaultAsync(x => x.HostId == hostId, ct)
        ?? new PointsSettings { HostId = hostId };

    private PointAmount RandomPayout(string minimum, string maximum)
    {
        var min = PointAmount.ParseAbsolute(minimum).Value / 10;
        var max = PointAmount.ParseAbsolute(maximum).Value / 10;
        var range = max - min;
        var offset =
            range <= int.MaxValue ? random.Next(0, (int)range + 1) : random.Next(0, int.MaxValue);
        return new PointAmount((min + offset) * 10);
    }

    private static PointOperationResult Reply(
        bool success,
        string message,
        PointsSettings settings
    ) => new(success, Format(message, settings));

    private static PointsGiveawayView ToView(PointsGiveaway giveaway) =>
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
