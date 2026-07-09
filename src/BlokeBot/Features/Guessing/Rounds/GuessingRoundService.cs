using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Replies;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Rounds;

public sealed class GuessingRoundService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    GuessingChangeNotifier changes
)
{
    public async Task<GuessingOperationResult> DeclareWinnerAsync(
        int hostId,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var round = await GuessingRoundQueries.Unresolved(db, hostId).FirstOrDefaultAsync(ct);
        var resolution =
            await GuessingProfileQueries.ReplySettingsResolutionForRoundOrProfileOrDefaultAsync(
                db,
                hostId,
                round,
                null,
                ct
            );
        var settings = resolution.Settings;
        var delivery = resolution.ReplyDelivery;
        var normalizedName = GuessName.Parse(name).Value;

        if (round is null)
            return new GuessingOperationResult(
                false,
                settings.NoOpenRoundReply,
                delivery.TargetFor(GuessingReplyKeys.NoOpenRound)
            );

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

        round.Status = GuessRoundStatus.Completed;
        round.ClosedAtUtc ??= DateTime.UtcNow;
        round.WinningName = normalizedName;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();

        var template = winners.Count == 0 ? settings.NoWinnersReply : settings.WinnerReply;
        var replyKey = winners.Count == 0 ? GuessingReplyKeys.NoWinners : GuessingReplyKeys.Winner;
        var message = MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = normalizedName,
                ["winners"] = winners.Count == 0 ? "none" : string.Join(", ", winners),
                ["count"] = winners.Count.ToString(),
            }
        );
        return new GuessingOperationResult(true, message, delivery.TargetFor(replyKey));
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

        var profile = await GuessingProfileQueries.LoadProfileWithSettingsAsync(
            db,
            hostId,
            profileId,
            ct,
            includeOptions: true
        );
        if (profile is null)
            return new GuessingOperationResult(false, "Round profile not found.");

        var settings = profile.ReplySettings!;
        var delivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyDeliveryFeature.Guessing,
            profile.Id,
            ct
        );
        if (await GuessingRoundQueries.Unresolved(db, hostId).AnyAsync(ct))
            return new GuessingOperationResult(
                false,
                settings.RoundAlreadyOpenReply,
                delivery.TargetFor(GuessingReplyKeys.RoundAlreadyOpen)
            );

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
        await changes.NotifyChangedAsync();
        return new GuessingOperationResult(
            true,
            FormatRoundStarted(
                settings.RoundStartedReply,
                profile.Name,
                FormatOptions(profile.Options.Select(x => x.Name))
            ),
            delivery.TargetFor(GuessingReplyKeys.RoundStarted)
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
            return NotConfigured();

        var profile = string.IsNullOrWhiteSpace(profileName)
            ? await GuessingProfileQueries.DefaultProfileWithSettingsAsync(db, hostId.Value, ct)
            : await GuessingProfileQueries.LoadProfileByNameAsync(
                db,
                hostId.Value,
                profileName,
                ct
            );

        if (profile is null)
            return new GuessingOperationResult(false, $"Unknown round profile: {profileName}.");

        return await StartRoundAsync(hostId.Value, profile.Id, ct);
    }

    public async Task<GuessingOperationResult> StopGuessingAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var round = await GuessingRoundQueries.Open(db, hostId).FirstOrDefaultAsync(ct);
        var resolution =
            await GuessingProfileQueries.ReplySettingsResolutionForRoundOrProfileOrDefaultAsync(
                db,
                hostId,
                round ?? await GuessingRoundQueries.Unresolved(db, hostId).FirstOrDefaultAsync(ct),
                null,
                ct
            );
        var settings = resolution.Settings;
        var delivery = resolution.ReplyDelivery;

        if (round is null)
            return new GuessingOperationResult(
                false,
                settings.NoOpenRoundReply,
                delivery.TargetFor(GuessingReplyKeys.NoOpenRound)
            );

        round.Status = GuessRoundStatus.Closed;
        round.ClosedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        return new GuessingOperationResult(
            true,
            settings.GuessingStoppedReply,
            delivery.TargetFor(GuessingReplyKeys.GuessingStopped)
        );
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

    private static string Format(string template, string name, string login) =>
        MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["login"] = login,
            }
        );

    private static string FormatRoundStarted(string template, string round, string options) =>
        MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["round"] = round,
                ["options"] = options,
            }
        );

    private static string FormatOptions(IEnumerable<string> options)
    {
        var values = options.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static GuessingOperationResult NotConfigured() =>
        new(false, "This channel is not set up.");
}
