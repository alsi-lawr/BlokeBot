using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.Replies;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Guesses;

public sealed class GuessingVoteService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    GuessingChangeNotifier changes
)
{
    public async Task<GuessingOperationResult> RecordGuessAsync(
        string hostLogin,
        string login,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await BotHostQueries.FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured();

        var round = await GuessingRoundQueries.Unresolved(db, hostId.Value).FirstOrDefaultAsync(ct);
        var resolution =
            await GuessingProfileQueries.ReplySettingsResolutionForRoundOrProfileOrDefaultAsync(
                db,
                hostId.Value,
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

        if (round.Status != GuessRoundStatus.Open)
            return new GuessingOperationResult(
                false,
                settings.GuessingClosedReply,
                delivery.TargetFor(GuessingReplyKeys.GuessingClosed)
            );

        var option = await db
            .GuessOptions.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.GuessRoundProfileId == round.GuessRoundProfileId && x.Name == normalizedName,
                ct
            );

        if (option is null)
        {
            return new GuessingOperationResult(
                false,
                Format(settings.InvalidGuessReply, normalizedName, login),
                delivery.TargetFor(GuessingReplyKeys.InvalidGuess)
            );
        }

        var vote = await db.Votes.SingleOrDefaultAsync(
            x => x.GuessRoundId == round.Id && x.Login == login,
            ct
        );

        if (vote is not null)
            return new GuessingOperationResult(false, string.Empty);

        db.Votes.Add(
            new GuessVote
            {
                GuessRoundId = round.Id,
                Login = login,
                GuessName = normalizedName,
                GuessedAtUtc = DateTime.UtcNow,
            }
        );

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        var answerReplyTarget = await AnswerReplyTargetAsync(db, round.GuessRoundProfileId, ct);
        return new GuessingOperationResult(
            true,
            Format(option.ReplyText, normalizedName, login),
            answerReplyTarget
        );
    }

    private static async Task<TwitchCommandResponseTarget> AnswerReplyTargetAsync(
        BlokeBotDbContext db,
        int profileId,
        CancellationToken ct
    ) =>
        await db
            .GuessOptions.AsNoTracking()
            .AnyAsync(
                x =>
                    x.GuessRoundProfileId == profileId
                    && x.ReplyTarget == ReplyDeliveryTargets.Whisper,
                ct
            )
            ? TwitchCommandResponseTarget.Whisper
            : TwitchCommandResponseTarget.Chat;

    private static string Format(string template, string name, string login) =>
        MessageTemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["login"] = login,
            }
        );

    private static GuessingOperationResult NotConfigured() =>
        new(false, "This channel is not set up.");
}
