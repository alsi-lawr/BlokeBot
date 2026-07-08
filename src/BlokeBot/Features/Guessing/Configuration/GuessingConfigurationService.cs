using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Configuration;

public sealed class GuessingConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    GuessingChangeNotifier changes
)
{
    public async Task<GuessingOperationResult> CreateProfileAsync(
        int hostId,
        string name,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var normalizedName = NormalizeDisplayName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return new GuessingOperationResult(false, "Profile name is required.");

        var slug = GuessRoundProfileSlug.FromName(normalizedName);
        if (await db.Profiles.AnyAsync(x => x.HostId == hostId && x.Slug == slug.Value, ct))
            return new GuessingOperationResult(false, "A profile with that name already exists.");

        db.Profiles.Add(
            new GuessRoundProfile
            {
                Name = normalizedName,
                Slug = slug.Value,
                HostId = hostId,
                IsDefault = false,
                ReplySettings = ToEntity(GuessingDefaults.Replies()),
            }
        );
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        return new GuessingOperationResult(true, $"Created {normalizedName}.");
    }

    public async Task<GuessingOperationResult> DeleteProfileAsync(
        int hostId,
        int profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var profile = await db.Profiles.SingleOrDefaultAsync(
            x => x.Id == profileId && x.HostId == hostId,
            ct
        );
        if (profile is null)
            return new GuessingOperationResult(false, "Profile not found.");

        if (await db.Profiles.CountAsync(x => x.HostId == hostId, ct) <= 1)
            return new GuessingOperationResult(false, "At least one profile is required.");

        if (await db.Rounds.AnyAsync(x => x.GuessRoundProfileId == profileId, ct))
            return new GuessingOperationResult(
                false,
                "Profiles with round history cannot be deleted."
            );

        var wasDefault = profile.IsDefault;
        db.Profiles.Remove(profile);
        await db.SaveChangesAsync(ct);

        if (wasDefault)
        {
            var nextDefault = await db
                .Profiles.Where(x => x.HostId == hostId)
                .OrderBy(x => x.Name)
                .FirstAsync(ct);
            nextDefault.IsDefault = true;
            await db.SaveChangesAsync(ct);
        }

        await changes.NotifyChangedAsync();
        return new GuessingOperationResult(true, $"Deleted {profile.Name}.");
    }

    public async Task<GuessingConfiguration> LoadConfigurationAsync(
        int hostId,
        int? profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var aliases = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        var profiles = await LoadProfileSummariesAsync(db, hostId, ct);
        var selectedProfileId =
            profileId is { } id && profiles.Any(x => x.Id == id)
                ? id
                : profiles.First(x => x.IsDefault).Id;

        return new GuessingConfiguration
        {
            Aliases = new CommandAliasEditor
            {
                StartAliases = JoinAliases(aliases, GuessCommandKind.Start),
                StopAliases = JoinAliases(aliases, GuessCommandKind.Stop),
                WinAliases = JoinAliases(aliases, GuessCommandKind.Win),
                GuessAliases = JoinAliases(aliases, GuessCommandKind.Guess),
                GuessesAliases = JoinAliases(aliases, GuessCommandKind.Guesses),
            },
            Profiles = profiles,
            Profile = await LoadProfileEditorAsync(db, hostId, selectedProfileId, ct),
        };
    }

    public async Task SaveConfigurationAsync(
        int hostId,
        GuessingConfiguration config,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        await SaveAliasesAsync(db, hostId, config.Aliases, ct);

        var profile = await db
            .Profiles.Include(x => x.ReplySettings)
            .Include(x => x.Options)
            .SingleAsync(x => x.Id == config.Profile.Id && x.HostId == hostId, ct);
        var profileName = NormalizeDisplayName(config.Profile.Name);
        if (string.IsNullOrWhiteSpace(profileName))
            profileName = profile.Name;

        var slug = GuessRoundProfileSlug.FromName(profileName);
        var duplicate = await db.Profiles.AnyAsync(
            x => x.HostId == hostId && x.Id != profile.Id && x.Slug == slug.Value,
            ct
        );
        if (duplicate)
            throw new InvalidOperationException("A profile with that name already exists.");

        profile.Name = profileName;
        profile.Slug = slug.Value;
        profile.IsDefault = config.Profile.IsDefault;

        if (profile.IsDefault)
        {
            await db
                .Profiles.Where(x => x.HostId == hostId && x.Id != profile.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.IsDefault, false), ct);
        }
        else if (
            !await db.Profiles.AnyAsync(
                x => x.HostId == hostId && x.Id != profile.Id && x.IsDefault,
                ct
            )
        )
        {
            profile.IsDefault = true;
        }

        profile.ReplySettings ??= ToEntity(GuessingDefaults.Replies());
        Apply(profile.ReplySettings, config.Profile.Replies);

        db.GuessOptions.RemoveRange(profile.Options);
        foreach (
            var option in config
                .Profile.Options.Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => GuessName.Parse(x.Name).Value)
                .Select(x => x.First())
        )
        {
            db.GuessOptions.Add(
                new GuessOption
                {
                    GuessRoundProfile = profile,
                    Name = GuessName.Parse(option.Name).Value,
                    ReplyText = string.IsNullOrWhiteSpace(option.ReplyText)
                        ? option.Name.Trim()
                        : option.ReplyText.Trim(),
                }
            );
        }

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    private static void AddAliases(
        List<CommandAlias> rows,
        int hostId,
        GuessCommandKind kind,
        string aliases
    )
    {
        foreach (var alias in SplitAliases(aliases))
            rows.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    Kind = Store(kind),
                    Alias = alias,
                }
            );
    }

    private static void Apply(BotReplySettings settings, ReplySettingsEditor editor)
    {
        settings.RoundStartedReply = editor.RoundStartedReply.Trim();
        settings.RoundAlreadyOpenReply = editor.RoundAlreadyOpenReply.Trim();
        settings.NoOpenRoundReply = editor.NoOpenRoundReply.Trim();
        settings.GuessingStoppedReply = editor.GuessingStoppedReply.Trim();
        settings.GuessingAlreadyStoppedReply = editor.GuessingAlreadyStoppedReply.Trim();
        settings.GuessingClosedReply = editor.GuessingClosedReply.Trim();
        settings.InvalidGuessReply = editor.InvalidGuessReply.Trim();
        settings.GuessUsageReply = editor.GuessUsageReply.Trim();
        settings.AvailableGuessesReply = editor.AvailableGuessesReply.Trim();
        settings.WinUsageReply = editor.WinUsageReply.Trim();
        settings.ModeratorOnlyReply = editor.ModeratorOnlyReply.Trim();
        settings.WinnerReply = editor.WinnerReply.Trim();
        settings.NoWinnersReply = editor.NoWinnersReply.Trim();
    }

    private static string JoinAliases(List<CommandAlias> aliases, GuessCommandKind kind) =>
        string.Join(", ", aliases.Where(x => x.Kind == Store(kind)).Select(x => x.Alias).Order());

    private static async Task<GuessRoundProfileEditor> LoadProfileEditorAsync(
        BlokeBotDbContext db,
        int hostId,
        int profileId,
        CancellationToken ct
    )
    {
        var profile =
            await db
                .Profiles.AsNoTracking()
                .Include(x => x.ReplySettings)
                .Include(x => x.Options)
                .SingleOrDefaultAsync(x => x.Id == profileId && x.HostId == hostId, ct)
            ?? throw new InvalidOperationException("Round profile not found.");

        return new GuessRoundProfileEditor
        {
            Id = profile.Id,
            Name = profile.Name,
            IsDefault = profile.IsDefault,
            Replies = ToEditor(profile.ReplySettings ?? ToEntity(GuessingDefaults.Replies())),
            Options = profile
                .Options.OrderBy(x => x.Name)
                .Select(x => new GuessOptionEditor { Name = x.Name, ReplyText = x.ReplyText })
                .ToList(),
        };
    }

    private async Task<List<GuessRoundProfileSummary>> LoadProfileSummariesAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .Profiles.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new GuessRoundProfileSummary(x.Id, x.Name, x.IsDefault))
            .ToListAsync(ct);

    private static string NormalizeDisplayName(string name) => name.Trim();

    private async Task SaveAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        CommandAliasEditor aliases,
        CancellationToken ct
    )
    {
        var rows = new List<CommandAlias>();
        AddAliases(rows, hostId, GuessCommandKind.Start, aliases.StartAliases);
        AddAliases(rows, hostId, GuessCommandKind.Stop, aliases.StopAliases);
        AddAliases(rows, hostId, GuessCommandKind.Win, aliases.WinAliases);
        AddAliases(rows, hostId, GuessCommandKind.Guess, aliases.GuessAliases);
        AddAliases(rows, hostId, GuessCommandKind.Guesses, aliases.GuessesAliases);

        var duplicate = rows.GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Alias !{duplicate.Key} is used more than once.");

        var guessKinds = Enum.GetValues<GuessCommandKind>().Select(Store).ToArray();
        var existingAliases = rows.Select(x => x.Alias).ToArray();
        var existingCollision = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && !guessKinds.Contains(x.Kind))
            .Where(x => existingAliases.Contains(x.Alias))
            .Select(x => x.Alias)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(existingCollision))
            throw new InvalidOperationException(
                $"Alias !{existingCollision} is already used by another bot function."
            );

        db.CommandAliases.RemoveRange(
            db.CommandAliases.Where(x => x.HostId == hostId && guessKinds.Contains(x.Kind))
        );
        db.CommandAliases.AddRange(rows);
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<string> SplitAliases(string aliases) =>
        CommandAliasNormalizer.Split(aliases);

    private static string Store(GuessCommandKind kind) => kind.ToString();

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

    private static ReplySettingsEditor ToEditor(BotReplySettings settings) =>
        new()
        {
            RoundStartedReply = settings.RoundStartedReply,
            RoundAlreadyOpenReply = settings.RoundAlreadyOpenReply,
            NoOpenRoundReply = settings.NoOpenRoundReply,
            GuessingStoppedReply = settings.GuessingStoppedReply,
            GuessingAlreadyStoppedReply = settings.GuessingAlreadyStoppedReply,
            GuessingClosedReply = settings.GuessingClosedReply,
            InvalidGuessReply = settings.InvalidGuessReply,
            GuessUsageReply = settings.GuessUsageReply,
            AvailableGuessesReply = string.IsNullOrWhiteSpace(settings.AvailableGuessesReply)
                ? GuessingDefaults.Replies().AvailableGuessesReply
                : settings.AvailableGuessesReply,
            WinUsageReply = settings.WinUsageReply,
            ModeratorOnlyReply = settings.ModeratorOnlyReply,
            WinnerReply = settings.WinnerReply,
            NoWinnersReply = settings.NoWinnersReply,
        };
}
