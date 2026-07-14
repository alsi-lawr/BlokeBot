using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Replies;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Rounds;

public sealed class GuessingRoundService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    GuessingChangeNotifier changes,
    PointBalanceService balances,
    PointsChangeNotifier pointsChanges
)
{
    public async Task<GuessingOperationResult> DeclareWinnerAsync(
        int hostId,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var round = await GuessingRoundQueries.LoadTrackedUnresolvedAsync(db, hostId, ct);
        var resolution = round is null
            ? await GuessingReplySettingsQueries.LoadForDefaultAsync(db, hostId, ct)
            : await GuessingReplySettingsQueries.LoadForRoundAsync(
                db,
                hostId,
                round.GuessRoundProfileId,
                ct
            );
        var settings = resolution.Settings;
        var delivery = resolution.ReplyDelivery;
        var normalizedName = GuessName.Parse(name).Value;

        if (round is null)
        {
            return new GuessingOperationResult(
                false,
                settings.NoOpenRoundReply,
                delivery.TargetFor(GuessingReplyKeys.NoOpenRound)
            );
        }

        var optionExists = await db.GuessOptions.AnyAsync(
            x => x.GuessRoundProfileId == round.GuessRoundProfileId && x.Name == normalizedName,
            ct
        );
        if (!optionExists)
        {
            return new GuessingOperationResult(
                false,
                Format(settings.InvalidGuessReply, normalizedName, string.Empty),
                delivery.TargetFor(GuessingReplyKeys.InvalidGuess)
            );
        }

        var winners = await db
            .Votes.AsNoTracking()
            .Where(x => x.GuessRoundId == round.Id && x.GuessName == normalizedName)
            .OrderBy(x => x.GuessedAtUtc)
            .Select(x => x.Login)
            .ToListAsync(ct);
        var reward = await db
            .Profiles.AsNoTracking()
            .Where(x => x.Id == round.GuessRoundProfileId)
            .Select(x => x.WinningGuessPointReward)
            .SingleAsync(ct);
        var rewardAmount = PointAmount.ParseAbsolute(reward);
        var pointLabel =
            await db
                .PointsSettings.AsNoTracking()
                .Where(x => x.HostId == hostId)
                .Select(x => x.PointLabel)
                .SingleOrDefaultAsync(ct)
            ?? "points";
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        round.Status = GuessRoundStatus.Completed;
        round.ClosedAtUtc ??= now;
        round.WinningName = normalizedName;
        var awardedAnyPoints = false;
        if (!rewardAmount.IsZero)
        {
            foreach (var winner in winners)
            {
                var result = await balances
                    .AwardGuessWin(db, hostId, round.Id, winner, rewardAmount, now)
                    .ExecuteAsync(ct);
                awardedAnyPoints = result.Match(_ => true, _ => awardedAnyPoints);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await changes.NotifyChangedAsync(ct);
        if (awardedAnyPoints)
        {
            await pointsChanges.NotifyChangedAsync(ct);
        }

        var message = MessageTemplateFormatter.Format(
            winners.Count == 0 ? settings.NoWinnersReply : settings.WinnerReply,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = normalizedName,
                ["winners"] = winners.Count == 0 ? "none" : string.Join(", ", winners),
                ["count"] = winners.Count.ToString(),
                ["reward"] = rewardAmount.ToDisplayString(),
                ["label"] = pointLabel,
                ["reward_text"] =
                    rewardAmount.IsZero || winners.Count == 0
                        ? string.Empty
                        : $" Each winner gets {rewardAmount.ToDisplayString()} {pointLabel}.",
            }
        );
        return new GuessingOperationResult(true, message);
    }

    public async Task<GuessingOperationResult> DeclareWinnerAsync(
        string hostLogin,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        return hostId is null ? NotConfigured() : await DeclareWinnerAsync(hostId.Value, name, ct);
    }

    public async Task<GuessingOperationResult> StartRoundAsync(
        int hostId,
        int profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var profile = await db.Profiles.LoadProfileWithOptionsAsync(hostId, profileId, ct);
        if (profile is null)
        {
            return new GuessingOperationResult(false, "Round type not found.");
        }

        var settings = profile.Settings;
        var delivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyFeature.Guessing,
            profile.Id,
            ct
        );
        if (await GuessingRoundQueries.HasUnresolvedAsync(db, hostId, ct))
        {
            return new GuessingOperationResult(
                false,
                settings.RoundAlreadyOpenReply,
                delivery.TargetFor(GuessingReplyKeys.RoundAlreadyOpen)
            );
        }

        db.Rounds.Add(
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfileId = profile.Id,
                Status = GuessRoundStatus.Open,
                StartedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return new GuessingOperationResult(
            true,
            FormatRoundStarted(
                settings.RoundStartedReply,
                profile.Name,
                FormatOptions(profile.OptionNames)
            )
        );
    }

    public async Task<GuessingOperationResult> StartRoundAsync(
        string hostLogin,
        string? profileName,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
        {
            return NotConfigured();
        }

        var profileId = string.IsNullOrWhiteSpace(profileName)
            ? await db.Profiles.LoadDefaultProfileIdAsync(hostId.Value, ct)
            : await db.Profiles.LoadProfileIdByNameAsync(hostId.Value, profileName, ct);

        if (profileId is null)
        {
            return new GuessingOperationResult(false, $"Unknown round type: {profileName}.");
        }

        return await StartRoundAsync(hostId.Value, profileId.Value, ct);
    }

    public async Task<GuessingOperationResult> StopGuessingAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var round = await GuessingRoundQueries.LoadTrackedOpenAsync(db, hostId, ct);
        var settingsRound =
            round ?? await GuessingRoundQueries.LoadTrackedUnresolvedAsync(db, hostId, ct);
        var resolution = settingsRound is null
            ? await GuessingReplySettingsQueries.LoadForDefaultAsync(db, hostId, ct)
            : await GuessingReplySettingsQueries.LoadForRoundAsync(
                db,
                hostId,
                settingsRound.GuessRoundProfileId,
                ct
            );
        var settings = resolution.Settings;
        var delivery = resolution.ReplyDelivery;

        if (round is null)
        {
            return new GuessingOperationResult(
                false,
                settings.NoOpenRoundReply,
                delivery.TargetFor(GuessingReplyKeys.NoOpenRound)
            );
        }

        round.Status = GuessRoundStatus.Closed;
        round.ClosedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return new GuessingOperationResult(true, settings.GuessingStoppedReply);
    }

    public async Task<GuessingOperationResult> StopGuessingAsync(
        string hostLogin,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        return hostId is null ? NotConfigured() : await StopGuessingAsync(hostId.Value, ct);
    }

    private static string Format(string template, string name, string login)
    {
        return MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["login"] = login,
            }
        );
    }

    private static string FormatRoundStarted(string template, string round, string options)
    {
        return MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["round"] = round,
                ["options"] = options,
            }
        );
    }

    private static string FormatOptions(IEnumerable<string> options)
    {
        var values = options.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static GuessingOperationResult NotConfigured()
    {
        return new(false, "This channel is not set up.");
    }
}
