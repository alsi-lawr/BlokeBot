using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Identity;
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
        var hostId = await FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured();

        var round = await UnresolvedRoundQuery(db, hostId.Value).FirstOrDefaultAsync(ct);
        var settings = await SettingsForRoundOrDefaultAsync(db, hostId.Value, round, ct);
        var normalizedName = GuessName.Parse(name).Value;

        if (round is null)
            return new GuessingOperationResult(false, settings.NoOpenRoundReply);

        if (round.Status != GuessRoundStatus.Open)
            return new GuessingOperationResult(false, settings.GuessingClosedReply);

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
                Format(settings.InvalidGuessReply, normalizedName, login)
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
        return new GuessingOperationResult(true, Format(option.ReplyText, normalizedName, login));
    }

    private static async Task<int> DefaultProfileIdAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .Profiles.Where(x => x.HostId == hostId && x.IsDefault)
            .Select(x => x.Id)
            .FirstAsync(ct);

    private static async Task<int?> FindHostIdAsync(
        BlokeBotDbContext db,
        string login,
        CancellationToken ct
    )
    {
        var normalized = LoginName.Parse(login);
        return await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == normalized.Value)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
    }

    private static string Format(string template, string name, string login) =>
        TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["login"] = login,
            }
        );

    private static async Task<GuessRoundProfile?> LoadProfileWithSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        int profileId,
        CancellationToken ct
    ) =>
        await db
            .Profiles.Include(x => x.ReplySettings)
            .SingleOrDefaultAsync(x => x.Id == profileId && x.HostId == hostId, ct);

    private static GuessingOperationResult NotConfigured() =>
        new(false, "This channel is not configured.");

    private static async Task<BotReplySettings> SettingsForRoundOrDefaultAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessRound? round,
        CancellationToken ct
    )
    {
        var profileId = round?.GuessRoundProfileId ?? await DefaultProfileIdAsync(db, hostId, ct);
        var profile = await LoadProfileWithSettingsAsync(db, hostId, profileId, ct);
        return profile?.ReplySettings ?? ToEntity(GuessingDefaults.Replies());
    }

    private static BotReplySettings ToEntity(ReplySettingsEditor editor) =>
        new()
        {
            RoundStartedReply = editor.RoundStartedReply,
            RoundAlreadyOpenReply = editor.RoundAlreadyOpenReply,
            NoOpenRoundReply = editor.NoOpenRoundReply,
            GuessingStoppedReply = editor.GuessingStoppedReply,
            GuessingAlreadyStoppedReply = editor.GuessingAlreadyStoppedReply,
            GuessingClosedReply = editor.GuessingClosedReply,
            InvalidGuessReply = editor.InvalidGuessReply,
            GuessUsageReply = editor.GuessUsageReply,
            AvailableGuessesReply = editor.AvailableGuessesReply,
            WinUsageReply = editor.WinUsageReply,
            ModeratorOnlyReply = editor.ModeratorOnlyReply,
            WinnerReply = editor.WinnerReply,
            NoWinnersReply = editor.NoWinnersReply,
        };

    private static IQueryable<GuessRound> UnresolvedRoundQuery(BlokeBotDbContext db, int hostId) =>
        db
            .Rounds.Where(x => x.HostId == hostId)
            .Where(x => x.Status == GuessRoundStatus.Open || x.Status == GuessRoundStatus.Closed)
            .OrderByDescending(x => x.StartedAtUtc);
}
