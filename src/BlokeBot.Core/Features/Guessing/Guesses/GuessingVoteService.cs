using System.Diagnostics;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Guesses;

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
        var submittedName = GuessName.Parse(name);

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

        var options = await db
            .GuessOptions.AsNoTracking()
            .Where(option => option.GuessRoundProfileId == round.ProfileId)
            .ToListAsync(ct);
        var option = options
            .Select(option => new { Option = option, Names = GuessAnswerNames.Parse(option.Name) })
            .FirstOrDefault(candidate => candidate.Names.Contains(submittedName));

        if (option is null)
        {
            return new GuessingOperationOutcome.Rejected(
                Format(settings.InvalidGuessReply, submittedName.Value, login),
                delivery.TargetFor(GuessingReplyKeys.InvalidGuess)
            );
        }

        var canonicalName = option.Names.Canonical.Value;

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
                GuessName = canonicalName,
                GuessedAtUtc = DateTime.UtcNow,
            }
        );

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(hostId.Value, ct);
        var answerReplyTarget = await AnswerReplyTargetAsync(db, round.ProfileId, ct);
        return new GuessingOperationOutcome.Succeeded(
            Format(option.Option.ReplyText, canonicalName, login),
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
