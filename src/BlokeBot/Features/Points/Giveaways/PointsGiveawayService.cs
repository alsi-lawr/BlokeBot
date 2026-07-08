using Alsi.TwitchBot;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Points.Replies;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Giveaways;

public sealed class PointsGiveawayService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointBalanceService balances,
    HostBotStatusService botStatus,
    IPointsRandom random,
    IServiceProvider services,
    PointsChangeNotifier changes
)
{
    private readonly object scheduleGate = new();
    private readonly Dictionary<int, CancellationTokenSource> schedules = [];

    public async Task<PointsGiveawayView?> GetActiveGiveawayAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .PointsGiveaways.AsNoTracking()
            .Include(x => x.Entrants)
            .Include(x => x.Winners)
            .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active.ToString())
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

        if (settings.GiveawayEligibility.Equals("followers", StringComparison.OrdinalIgnoreCase))
        {
            var status = await botStatus.GetStatusAsync(hostLogin, ct);
            if (status.ModeratorState != HostBotModeratorState.IsModerator)
                return Reply(false, settings.FollowerEligibilityUnavailableReply, settings);
        }

        var giveaway = new PointsGiveaway
        {
            HostId = hostId,
            Status = PointsGiveawayStatus.Active.ToString(),
            StartedAtUtc = now,
            EndsAtUtc = now.AddSeconds(Math.Max(1, settings.GiveawayDurationSeconds)),
            MinimumPayout = settings.GiveawayMinimumPayout,
            MaximumPayout = settings.GiveawayMaximumPayout,
            WinnerCount = Math.Max(1, settings.GiveawayWinnerCount),
            Eligibility = settings.GiveawayEligibility,
        };
        db.PointsGiveaways.Add(giveaway);
        await db.SaveChangesAsync(ct);
        Schedule(
            giveaway.Id,
            hostLogin,
            giveaway.EndsAtUtc,
            settings.GiveawayDurationSeconds,
            reply
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
            .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active.ToString())
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
        var result = await DrawActiveAsync(hostId, hostLogin, ct);
        await changes.NotifyChangedAsync();
        return result;
    }

    public async Task<PointOperationResult> CancelAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await LoadSettingsAsync(db, hostId, ct);
        var giveaway = await db
            .PointsGiveaways.Where(x =>
                x.HostId == hostId && x.Status == PointsGiveawayStatus.Active.ToString()
            )
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (giveaway is null)
            return Reply(false, settings.GiveawayNotActiveReply, settings);

        giveaway.Status = PointsGiveawayStatus.Cancelled.ToString();
        giveaway.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        CancelSchedule(giveaway.Id);
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
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var settings = await LoadSettingsAsync(db, hostId, ct);
        var giveaway = await db
            .PointsGiveaways.Include(x => x.Entrants)
            .Include(x => x.Winners)
            .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active.ToString())
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (giveaway is null)
            return Reply(false, settings.GiveawayNotActiveReply, settings);

        giveaway.Status = PointsGiveawayStatus.Completed.ToString();
        giveaway.CompletedAtUtc = DateTime.UtcNow;
        var entrants = giveaway
            .Entrants.Select(x => x.Login)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (entrants.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            CancelSchedule(giveaway.Id);
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
                hostId,
                giveaway.Id,
                winner,
                payout,
                DateTime.UtcNow,
                ct
            );
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        CancelSchedule(giveaway.Id);
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

    private async Task SendUpdateAsync(
        int giveawayId,
        string hostLogin,
        DateTime endsAtUtc,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var giveaway = await db
            .PointsGiveaways.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == giveawayId, ct);
        if (giveaway is null || giveaway.Status != PointsGiveawayStatus.Active.ToString())
            return;

        var settings = await LoadSettingsAsync(db, giveaway.HostId, ct);
        var message = Format(
            settings.GiveawayUpdateReply,
            settings,
            timeLeft: FormatTimeLeft(endsAtUtc - DateTime.UtcNow)
        );
        await SendChatAsync(hostLogin, message, ct);
    }

    private void Schedule(
        int giveawayId,
        string hostLogin,
        DateTime endsAtUtc,
        int durationSeconds,
        Func<string, CancellationToken, ValueTask>? reply
    )
    {
        var cts = new CancellationTokenSource();
        lock (scheduleGate)
        {
            CancelSchedule(giveawayId);
            schedules[giveawayId] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var scheduledAtUtc = DateTime.UtcNow;
                foreach (var elapsedFactor in new[] { 0.25, 0.5, 0.75 })
                {
                    var targetUtc = scheduledAtUtc.AddSeconds(durationSeconds * elapsedFactor);
                    var delay = targetUtc - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, cts.Token);

                    await SendUpdateAsync(giveawayId, hostLogin, endsAtUtc, cts.Token);
                }

                var remaining = endsAtUtc - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cts.Token);

                await using var db = await dbFactory.CreateDbContextAsync(cts.Token);
                var hostId = await db
                    .PointsGiveaways.AsNoTracking()
                    .Where(x => x.Id == giveawayId)
                    .Select(x => (int?)x.HostId)
                    .SingleOrDefaultAsync(cts.Token);
                if (hostId is null)
                    return;

                var result = await DrawActiveAsync(hostId.Value, hostLogin, cts.Token);
                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    if (reply is not null)
                        await reply(result.Message, cts.Token);
                    else
                        await SendChatAsync(hostLogin, result.Message, cts.Token);
                }

                await changes.NotifyChangedAsync();
            }
            catch (OperationCanceledException) { }
        });
    }

    private void CancelSchedule(int giveawayId)
    {
        lock (scheduleGate)
        {
            if (!schedules.Remove(giveawayId, out var cts))
                return;

            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task SendChatAsync(string hostLogin, string message, CancellationToken ct)
    {
        var sender = services.GetService<ITwitchChatMessageSender>();
        if (sender is not null)
            await sender.SendAsync(hostLogin, message, ct);
    }

    private async Task<FollowerCheckResult> CheckEligibilityAsync(
        PointsSettings settings,
        string hostLogin,
        string login,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        var mode = settings.GiveawayEligibility.ToLowerInvariant();
        if (mode == "subscribers")
            return HasSubscriberBadge(tags)
                ? FollowerCheckResult.Eligible
                : FollowerCheckResult.NotEligible;

        if (mode == "followers")
            return await botStatus.IsFollowerAsync(hostLogin, login, ct);

        return FollowerCheckResult.Eligible;
    }

    private static string Format(
        string template,
        PointsSettings settings,
        string? user = null,
        string? winners = null,
        string? timeLeft = null
    ) =>
        PointsTemplateFormatter.Format(
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
            x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active.ToString(),
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
            Enum.TryParse<PointsGiveawayStatus>(giveaway.Status, out var status)
                ? status
                : PointsGiveawayStatus.Active,
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
