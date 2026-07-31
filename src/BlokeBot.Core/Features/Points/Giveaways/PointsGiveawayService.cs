using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Core.Identity;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.Giveaways;

public sealed class PointsGiveawayService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PointsGiveawayDrawService draws,
    PointsGiveawayEligibilityPolicy eligibility,
    PointsGiveawayMessageFormatter formatter,
    IPointsGiveawayScheduler scheduler,
    PointsGiveawayChangeNotifier changes
)
{
    public IO<Option<PointsGiveawayView>, Never> GetActiveGiveaway(int hostId)
    {
        return IO<Option<PointsGiveawayView>, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return Result<Option<PointsGiveawayView>, Never>.Success(
                Option<PointsGiveawayView>.FromNullable(
                    await PointsGiveawayQueries.LoadActiveViewAsync(db, hostId, ct)
                )
            );
        });
    }

    public async Task<PointOperationOutcome> StartAsync(
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
        {
            return new PointsGiveawayStartOutcome.AlreadyActive(settings);
        }

        var configurationFailure = StartConfigurationFailure(settings);
        if (configurationFailure is not null)
        {
            return new PointsGiveawayStartOutcome.InvalidConfiguration(
                settings,
                configurationFailure
            );
        }

        var now = DateTime.UtcNow;
        var cooldownStart = now.AddSeconds(-settings.GiveawayCooldownSeconds);
        var lastStartedResult = await PointsGiveawayQueries
            .FindLastStartedAfter(db, hostId, cooldownStart)
            .ExecuteAsync(ct);
        var lastStarted = lastStartedResult.Match(
            option => option.Match<DateTime?>(value => value, () => null),
            _ => throw new UnreachableException()
        );
        if (lastStarted is not null)
        {
            var readyAt = lastStarted.Value.AddSeconds(settings.GiveawayCooldownSeconds);
            return new PointsGiveawayStartOutcome.Cooldown(settings, readyAt - now);
        }

        var livenessResult = await eligibility.GetStreamLiveness(hostLogin).ExecuteAsync(ct);
        var liveness = livenessResult.Match(value => value, _ => throw new UnreachableException());
        switch (liveness)
        {
            case HostStreamLivenessOutcome.Live:
                break;
            case HostStreamLivenessOutcome.Offline:
                return new PointsGiveawayStartOutcome.StreamOffline(settings);
            case HostStreamLivenessOutcome.Unavailable unavailable:
                return new PointsGiveawayStartOutcome.StreamLivenessUnavailable(
                    settings,
                    unavailable
                );
            default:
                throw new UnreachableException("Unknown stream-liveness outcome.");
        }

        if (!await eligibility.IsFollowerEligibilityAvailableAsync(hostLogin, settings, ct))
        {
            return new PointsGiveawayStartOutcome.FollowerEligibilityUnavailable(settings);
        }

        var giveaway = new PointsGiveaway
        {
            HostId = hostId,
            Status = PointsGiveawayStatus.Active,
            StartedAtUtc = now,
            EndsAtUtc = now.AddSeconds(settings.GiveawayDurationSeconds),
            MinimumPayout = settings.GiveawayMinimumPayout,
            MaximumPayout = settings.GiveawayMaximumPayout,
            WinnerCount = settings.GiveawayWinnerCount,
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
        await changes.NotifyChangedAsync(hostId, ct);
        return new PointsGiveawayStartOutcome.Started(settings);
    }

    private static PointsConfigurationValidationError? StartConfigurationFailure(
        PointsSettings settings
    )
    {
        if (settings.GiveawayDurationSeconds < 1)
        {
            return new PointsConfigurationValidationError.GiveawayDurationBelowMinimum();
        }

        return settings.GiveawayWinnerCount < 1
            ? new PointsConfigurationValidationError.GiveawayWinnerCountBelowMinimum()
            : null;
    }

    public async Task<PointOperationOutcome> JoinAsync(
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
        {
            return new PointsGiveawayJoinOutcome.NotActive(settings, normalized);
        }

        if (giveaway.Entrants.Any(x => x.Login == normalized))
        {
            return new PointsGiveawayJoinOutcome.DuplicateJoin(settings, normalized);
        }

        var joinEligibilityResult = await eligibility
            .CheckJoinEligibility(settings, hostLogin, normalized, tags)
            .ExecuteAsync(ct);
        var joinEligibility = joinEligibilityResult.Match(
            value => value,
            _ => throw new UnreachableException()
        );
        if (joinEligibility is FollowerCheckOutcome.Unavailable)
        {
            return new PointsGiveawayJoinOutcome.FollowerEligibilityUnavailable(
                settings,
                normalized
            );
        }

        if (joinEligibility is FollowerCheckOutcome.NotEligible)
        {
            return new PointsGiveawayJoinOutcome.NotEligible(settings, normalized);
        }

        db.PointsGiveawayEntrants.Add(
            new PointsGiveawayEntrant
            {
                GiveawayId = giveaway.Id,
                Login = normalized,
                JoinedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(hostId, ct);
        return new PointsGiveawayJoinOutcome.Joined(settings, normalized);
    }

    public async Task<PointOperationOutcome> EndAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    )
    {
        var outcome = await DrawActiveOutcomeAsync(hostId, hostLogin, ct);
        return formatter.Reply(outcome, await LoadReplyDeliveryAsync(hostId, ct));
    }

    public async Task<PointOperationOutcome> CancelAsync(int hostId, CancellationToken ct)
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
        {
            return new PointsGiveawayCancelOutcome.NotActive(settings);
        }

        giveaway.Status = PointsGiveawayStatus.Cancelled;
        giveaway.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        scheduler.Cancel(giveaway.Id);
        await changes.NotifyChangedAsync(hostId, ct);
        return new PointsGiveawayCancelOutcome.Cancelled(settings);
    }

    private async Task<PointsGiveawayDrawOutcome> DrawActiveOutcomeAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await PointsGiveawayQueries.LoadSettingsAsync(db, hostId, ct);
        var giveawayIdResult = await PointsGiveawayQueries
            .FindActiveGiveawayId(db, hostId)
            .ExecuteAsync(ct);
        var giveawayId = giveawayIdResult.Match(
            option => option.Match<int?>(value => value, () => null),
            _ => throw new UnreachableException()
        );
        if (giveawayId is null)
        {
            return new PointsGiveawayDrawOutcome.NotActive(settings);
        }

        var result = await draws.DrawOutcomeAsync(giveawayId.Value, ct);
        await result.Match(
            static _ => Task.CompletedTask,
            static _ => Task.CompletedTask,
            _ => CompleteAsync(),
            static _ => Task.CompletedTask,
            _ => CompleteAsync()
        );
        return result;

        async Task CompleteAsync()
        {
            scheduler.Cancel(giveawayId.Value);
            await changes.NotifyChangedAsync(hostId, ct);
        }
    }

    internal async Task<PointsGiveawayDrawOutcome> DrawOutcomeAsync(
        int giveawayId,
        CancellationToken ct
    )
    {
        return await draws.DrawOutcomeAsync(giveawayId, ct);
    }

    private async Task<ReplyDeliveryMap> LoadReplyDeliveryAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await PointsGiveawayQueries.LoadReplyDeliveryAsync(db, hostId, ct);
    }
}
