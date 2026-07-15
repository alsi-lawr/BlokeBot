using System.Diagnostics;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.Replies;
using BlokeBot.Functional;
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
    public IO<GuessingOperationOutcome, Never> RecordGuess(
        string hostLogin,
        string login,
        string name
    )
    {
        return IO<GuessingOperationOutcome, Never>.Create(async ct =>
            Result<GuessingOperationOutcome, Never>.Success(
                await PersistGuessAsync(hostLogin, login, name, ct)
            )
        );
    }

    private async Task<GuessingOperationOutcome> PersistGuessAsync(
        string hostLogin,
        string login,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostIdResult = await BotHostQueries.FindHostId(db, hostLogin).ExecuteAsync(ct);
        var hostId = hostIdResult.Match(
            option => option.Match<int?>(value => value, () => null),
            _ => throw new UnreachableException()
        );
        if (hostId is null)
        {
            return NotConfigured();
        }

        var round = await GuessingRoundQueries.LoadUnresolvedAsync(db, hostId.Value, ct);
        var resolution = round is null
            ? await GuessingReplySettingsQueries.LoadForDefaultAsync(db, hostId.Value, ct)
            : await GuessingReplySettingsQueries.LoadForRoundAsync(
                db,
                hostId.Value,
                round.ProfileId,
                ct
            );
        var settings = resolution.Settings;
        var delivery = resolution.ReplyDelivery;
        var normalizedName = GuessName.Parse(name).Value;

        if (round is null)
        {
            return new GuessingOperationOutcome.Rejected(
                settings.NoOpenRoundReply,
                delivery.TargetFor(GuessingReplyKeys.NoOpenRound)
            );
        }

        if (!round.Lifecycle.Match(static _ => true, static _ => false, static _ => false))
        {
            return new GuessingOperationOutcome.Rejected(
                settings.GuessingClosedReply,
                delivery.TargetFor(GuessingReplyKeys.GuessingClosed)
            );
        }

        var option = await db
            .GuessOptions.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.GuessRoundProfileId == round.ProfileId && x.Name == normalizedName,
                ct
            );

        if (option is null)
        {
            return new GuessingOperationOutcome.Rejected(
                Format(settings.InvalidGuessReply, normalizedName, login),
                delivery.TargetFor(GuessingReplyKeys.InvalidGuess)
            );
        }

        var vote = await db.Votes.SingleOrDefaultAsync(
            x => x.GuessRoundId == round.Id && x.Login == login,
            ct
        );

        if (vote is not null)
        {
            return new GuessingOperationOutcome.Rejected(string.Empty);
        }

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
        await changes.NotifyChangedAsync(ct);
        var answerReplyTarget = await AnswerReplyTargetAsync(db, round.ProfileId, ct);
        return new GuessingOperationOutcome.Succeeded(
            Format(option.ReplyText, normalizedName, login),
            answerReplyTarget
        );
    }

    private static async Task<CommandResponseTarget> AnswerReplyTargetAsync(
        BlokeBotDbContext db,
        int profileId,
        CancellationToken ct
    )
    {
        var targets = await db
            .GuessOptions.AsNoTracking()
            .Where(x => x.GuessRoundProfileId == profileId)
            .Select(x => x.ReplyTarget)
            .ToListAsync(ct);

        return targets.Any(x => x.IsWhisper())
            ? CommandResponseTarget.Whisper
            : CommandResponseTarget.Chat;
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

    private static GuessingOperationOutcome NotConfigured()
    {
        return new GuessingOperationOutcome.Rejected("This channel is not set up.");
    }
}
