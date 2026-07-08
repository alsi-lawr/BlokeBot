using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Identity;
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
        var round = await UnresolvedRoundQuery(db, hostId).FirstOrDefaultAsync(ct);
        var settings = await SettingsForRoundOrDefaultAsync(db, hostId, round, ct);
        var normalizedName = GuessName.Parse(name).Value;

        if (round is null)
            return new GuessingOperationResult(false, settings.NoOpenRoundReply);

        var optionExists = await db.GuessOptions.AnyAsync(
            x => x.GuessRoundProfileId == round.GuessRoundProfileId && x.Name == normalizedName,
            ct
        );
        if (!optionExists)
        {
            return new GuessingOperationResult(
                false,
                Format(settings.InvalidGuessReply, normalizedName, string.Empty)
            );
        }

        var winners = await db
            .Votes.AsNoTracking()
            .Where(x => x.GuessRoundId == round.Id && x.GuessName == normalizedName)
            .OrderBy(x => x.GuessedAtUtc)
            .Select(x => x.Login)
            .ToListAsync(ct);

        round.Status = Store(GuessRoundStatus.Completed);
        round.ClosedAtUtc ??= DateTime.UtcNow;
        round.WinningName = normalizedName;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();

        var template = winners.Count == 0 ? settings.NoWinnersReply : settings.WinnerReply;
        var message = TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = normalizedName,
                ["winners"] = winners.Count == 0 ? "none" : string.Join(", ", winners),
                ["count"] = winners.Count.ToString(),
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
        var hostId = await FindHostIdAsync(db, hostLogin, ct);
        return hostId is null ? NotConfigured() : await DeclareWinnerAsync(hostId.Value, name, ct);
    }

    public async Task<GuessingOperationResult> StartRoundAsync(
        int hostId,
        int profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var profile = await LoadProfileWithSettingsAsync(db, hostId, profileId, ct);
        if (profile is null)
            return new GuessingOperationResult(false, "Round profile not found.");

        var settings = profile.ReplySettings!;
        if (await OpenRoundQuery(db, hostId).AnyAsync(ct))
            return new GuessingOperationResult(false, settings.RoundAlreadyOpenReply);

        db.Rounds.Add(
            new GuessRound
            {
                GuessRoundProfileId = profile.Id,
                Status = Store(GuessRoundStatus.Open),
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
        var hostId = await FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured();

        var profile = string.IsNullOrWhiteSpace(profileName)
            ? await DefaultProfileAsync(db, hostId.Value, ct)
            : await LoadProfileByNameAsync(db, hostId.Value, profileName, ct);

        if (profile is null)
            return new GuessingOperationResult(false, $"Unknown round profile: {profileName}.");

        return await StartRoundAsync(hostId.Value, profile.Id, ct);
    }

    public async Task<GuessingOperationResult> StopGuessingAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var round = await OpenRoundQuery(db, hostId).FirstOrDefaultAsync(ct);
        var settings = await SettingsForRoundOrDefaultAsync(
            db,
            hostId,
            round ?? await UnresolvedRoundQuery(db, hostId).FirstOrDefaultAsync(ct),
            ct
        );

        if (round is null)
            return new GuessingOperationResult(false, settings.NoOpenRoundReply);

        round.Status = Store(GuessRoundStatus.Closed);
        round.ClosedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        return new GuessingOperationResult(true, settings.GuessingStoppedReply);
    }

    public async Task<GuessingOperationResult> StopGuessingAsync(
        string hostLogin,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await FindHostIdAsync(db, hostLogin, ct);
        return hostId is null ? NotConfigured() : await StopGuessingAsync(hostId.Value, ct);
    }

    private static async Task<GuessRoundProfile> DefaultProfileAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await LoadProfileWithSettingsAsync(
            db,
            hostId,
            await db
                .Profiles.Where(x => x.HostId == hostId && x.IsDefault)
                .Select(x => x.Id)
                .FirstAsync(ct),
            ct
        ) ?? throw new InvalidOperationException("Default profile is missing.");

    private static async Task<int> DefaultProfileIdAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) => (await DefaultProfileAsync(db, hostId, ct)).Id;

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

    private static string FormatRoundStarted(string template, string round, string options) =>
        TemplateFormatter.Format(
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

    private static async Task<GuessRoundProfile?> LoadProfileByNameAsync(
        BlokeBotDbContext db,
        int hostId,
        string profileName,
        CancellationToken ct
    ) =>
        await db
            .Profiles.AsNoTracking()
            .FirstOrDefaultAsync(
                x =>
                    x.HostId == hostId
                    && x.Slug == GuessRoundProfileSlug.FromName(profileName).Value,
                ct
            );

    private static async Task<GuessRoundProfile?> LoadProfileWithSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        int profileId,
        CancellationToken ct
    ) =>
        await db
            .Profiles.Include(x => x.ReplySettings)
            .Include(x => x.Options)
            .SingleOrDefaultAsync(x => x.Id == profileId && x.HostId == hostId, ct);

    private static GuessingOperationResult NotConfigured() =>
        new(false, "This channel is not configured.");

    private static IQueryable<GuessRound> OpenRoundQuery(BlokeBotDbContext db, int hostId) =>
        db
            .Rounds.Where(x => x.GuessRoundProfile != null && x.GuessRoundProfile.HostId == hostId)
            .Where(x => x.Status == Store(GuessRoundStatus.Open))
            .OrderByDescending(x => x.StartedAtUtc);

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

    private static string Store(GuessRoundStatus status) => status.ToString();

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
            .Rounds.Where(x => x.GuessRoundProfile != null && x.GuessRoundProfile.HostId == hostId)
            .Where(x =>
                x.Status == Store(GuessRoundStatus.Open)
                || x.Status == Store(GuessRoundStatus.Closed)
            )
            .OrderByDescending(x => x.StartedAtUtc);
}
