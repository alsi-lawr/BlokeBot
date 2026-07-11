using System.Diagnostics;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Replies;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Giveaways;

public sealed class PointsGiveawayService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointsGiveawayDrawService draws,
    PointsGiveawayEligibilityPolicy eligibility,
    PointsGiveawayMessageFormatter formatter,
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
            .Select(x => PointsGiveawayQueries.ToView(x))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PointOperationResult> StartAsync(
        int hostId,
        string hostLogin,
        Func<string, CancellationToken, ValueTask>? reply,
        CancellationToken ct
    )
    {
        var outcome = await StartOutcomeAsync(hostId, hostLogin, reply, ct);
        return formatter.Reply(outcome, await LoadReplyDeliveryAsync(hostId, ct));
    }

    internal async Task<PointsGiveawayStartOutcome> StartOutcomeAsync(
        int hostId,
        string hostLogin,
        Func<string, CancellationToken, ValueTask>? reply,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, hostId, ct);
        if (await PointsGiveawayQueries.HasActiveGiveawayAsync(db, hostId, ct))
            return new PointsGiveawayStartOutcome(
                PointsGiveawayStartOutcomeKind.AlreadyActive,
                settings
            );

        var now = DateTime.UtcNow;
        var cooldownStart = now.AddSeconds(-settings.GiveawayCooldownSeconds);
        var lastStarted = await PointsGiveawayQueries.FindLastStartedAfterAsync(
            db,
            hostId,
            cooldownStart,
            ct
        );
        if (lastStarted is not null)
        {
            var readyAt = lastStarted.Value.AddSeconds(settings.GiveawayCooldownSeconds);
            return new PointsGiveawayStartOutcome(
                PointsGiveawayStartOutcomeKind.Cooldown,
                settings,
                readyAt - now
            );
        }

        switch (await eligibility.GetStreamLivenessAsync(hostLogin, ct))
        {
            case HostStreamLivenessOutcome.Live:
                break;
            case HostStreamLivenessOutcome.Offline:
                return new PointsGiveawayStartOutcome(
                    PointsGiveawayStartOutcomeKind.StreamOffline,
                    settings
                );
            case HostStreamLivenessOutcome.Unavailable unavailable:
                return new PointsGiveawayStartOutcome(
                    PointsGiveawayStartOutcomeKind.StreamLivenessUnavailable,
                    settings,
                    StreamLivenessFailure: unavailable
                );
            default:
                throw new UnreachableException("Unknown stream-liveness outcome.");
        }

        if (!await eligibility.IsFollowerEligibilityAvailableAsync(hostLogin, settings, ct))
            return new PointsGiveawayStartOutcome(
                PointsGiveawayStartOutcomeKind.FollowerEligibilityUnavailable,
                settings
            );

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
        await changes.NotifyChangedAsync(ct);
        return new PointsGiveawayStartOutcome(PointsGiveawayStartOutcomeKind.Started, settings);
    }

    public async Task<PointOperationResult> JoinAsync(
        int hostId,
        string hostLogin,
        string login,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        var outcome = await JoinOutcomeAsync(hostId, hostLogin, login, tags, ct);
        return formatter.Reply(outcome, await LoadReplyDeliveryAsync(hostId, ct));
    }

    internal async Task<PointsGiveawayJoinOutcome> JoinOutcomeAsync(
        int hostId,
        string hostLogin,
        string login,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, hostId, ct);
        var giveaway = await db
            .PointsGiveaways.Include(x => x.Entrants)
            .Where(x => x.HostId == hostId && x.Status == PointsGiveawayStatus.Active)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        var normalized = LoginName.Parse(login).Value;
        if (giveaway is null)
            return new PointsGiveawayJoinOutcome(
                PointsGiveawayJoinOutcomeKind.NotActive,
                settings,
                normalized
            );

        if (giveaway.Entrants.Any(x => x.Login == normalized))
            return new PointsGiveawayJoinOutcome(
                PointsGiveawayJoinOutcomeKind.DuplicateJoin,
                settings,
                normalized
            );

        var joinEligibility = await eligibility.CheckJoinEligibilityAsync(
            settings,
            hostLogin,
            normalized,
            tags,
            ct
        );
        if (joinEligibility == FollowerCheckResult.Unavailable)
            return new PointsGiveawayJoinOutcome(
                PointsGiveawayJoinOutcomeKind.FollowerEligibilityUnavailable,
                settings,
                normalized
            );

        if (joinEligibility == FollowerCheckResult.NotEligible)
            return new PointsGiveawayJoinOutcome(
                PointsGiveawayJoinOutcomeKind.NotEligible,
                settings,
                normalized
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
        await changes.NotifyChangedAsync(ct);
        return new PointsGiveawayJoinOutcome(
            PointsGiveawayJoinOutcomeKind.Joined,
            settings,
            normalized
        );
    }

    public async Task<PointOperationResult> EndAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    )
    {
        var outcome = await DrawActiveOutcomeAsync(hostId, hostLogin, ct);
        return formatter.Reply(outcome, await LoadReplyDeliveryAsync(hostId, ct));
    }

    public async Task<PointOperationResult> CancelAsync(int hostId, CancellationToken ct)
    {
        var outcome = await CancelOutcomeAsync(hostId, ct);
        return formatter.Reply(outcome, await LoadReplyDeliveryAsync(hostId, ct));
    }

    internal async Task<PointsGiveawayCancelOutcome> CancelOutcomeAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, hostId, ct);
        var giveaway = await db
            .PointsGiveaways.Where(x =>
                x.HostId == hostId && x.Status == PointsGiveawayStatus.Active
            )
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (giveaway is null)
            return new PointsGiveawayCancelOutcome(
                PointsGiveawayCancelOutcomeKind.NotActive,
                settings
            );

        giveaway.Status = PointsGiveawayStatus.Cancelled;
        giveaway.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        scheduler.Cancel(giveaway.Id);
        await changes.NotifyChangedAsync(ct);
        return new PointsGiveawayCancelOutcome(PointsGiveawayCancelOutcomeKind.Cancelled, settings);
    }

    private async Task<PointsGiveawayDrawOutcome> DrawActiveOutcomeAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, hostId, ct);
        var giveawayId = await PointsGiveawayQueries.FindActiveGiveawayIdAsync(db, hostId, ct);
        if (giveawayId is null)
            return PointsGiveawayDrawOutcome.NotActive(settings);

        var result = await draws.DrawOutcomeAsync(giveawayId.Value, ct);
        if (result.Success)
        {
            scheduler.Cancel(giveawayId.Value);
            await changes.NotifyChangedAsync(ct);
        }

        return result;
    }

    internal async Task<PointsGiveawayDrawOutcome> DrawOutcomeAsync(
        int giveawayId,
        CancellationToken ct
    ) => await draws.DrawOutcomeAsync(giveawayId, ct);

    private async Task<ReplyDeliveryMap> LoadReplyDeliveryAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await PointsGiveawayQueries.LoadReplyDeliveryAsync(db, hostId, ct);
    }
}
