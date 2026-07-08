using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Commands;

public sealed class GuessingCommandService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<string> AvailableGuessesReplyAsync(string hostLogin, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured().Message;

        var round = await UnresolvedRoundQuery(db, hostId.Value).FirstOrDefaultAsync(ct);
        var profileId =
            round?.GuessRoundProfileId ?? await DefaultProfileIdAsync(db, hostId.Value, ct);
        var profile = await LoadProfileWithSettingsAsync(db, hostId.Value, profileId, ct);
        var settings = profile?.ReplySettings ?? ToEntity(GuessingDefaults.Replies());
        var template = string.IsNullOrWhiteSpace(settings.AvailableGuessesReply)
            ? GuessingDefaults.Replies().AvailableGuessesReply
            : settings.AvailableGuessesReply;

        return TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["round"] = profile?.Name ?? string.Empty,
                ["options"] = FormatOptions(profile?.Options.Select(x => x.Name) ?? []),
            }
        );
    }

    public async Task<string> ModeratorOnlyReplyAsync(string hostLogin, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured().Message;

        var settings = await SettingsForRoundOrDefaultAsync(
            db,
            hostId.Value,
            await UnresolvedRoundQuery(db, hostId.Value).FirstOrDefaultAsync(ct),
            ct
        );
        return settings.ModeratorOnlyReply;
    }

    public async Task<GuessCommandKind?> ResolveCommandAsync(
        string hostLogin,
        string alias,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return null;

        var normalized = CommandAliasNormalizer.Normalize(alias);
        var storedKind = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId.Value && x.Alias == normalized)
            .Select(x => x.Kind)
            .FirstOrDefaultAsync(ct);
        return ParseCommandKind(storedKind);
    }

    public async Task<string> UsageReplyAsync(
        string hostLogin,
        GuessCommandKind kind,
        string command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await FindHostIdAsync(db, hostLogin, ct);
        if (hostId is null)
            return NotConfigured().Message;

        var settings = await SettingsForRoundOrDefaultAsync(
            db,
            hostId.Value,
            await UnresolvedRoundQuery(db, hostId.Value).FirstOrDefaultAsync(ct),
            ct
        );
        var template = kind switch
        {
            GuessCommandKind.Win => settings.WinUsageReply,
            GuessCommandKind.Start => "Usage: !{command} [round]",
            _ => settings.GuessUsageReply,
        };
        return TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["command"] = command,
            }
        );
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

    private static GuessCommandKind? ParseCommandKind(string? value) =>
        Enum.TryParse<GuessCommandKind>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static string FormatOptions(IEnumerable<string> options)
    {
        var values = options.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

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
