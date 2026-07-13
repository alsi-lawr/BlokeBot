using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Configuration;

public sealed class GuessingConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CommandAliasRegistry aliasRegistry,
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
        {
            return new GuessingOperationResult(false, "Round type name is required.");
        }

        var slug = GuessRoundProfileSlug.FromName(normalizedName);
        if (await db.Profiles.AnyAsync(x => x.HostId == hostId && x.Slug == slug.Value, ct))
        {
            return new GuessingOperationResult(
                false,
                "A round type with that name already exists."
            );
        }

        db.Profiles.Add(
            new GuessRoundProfile
            {
                Name = normalizedName,
                Slug = slug.Value,
                HostId = hostId,
                IsDefault = false,
                ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
            }
        );
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
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
        {
            return new GuessingOperationResult(false, "Round type not found.");
        }

        if (await db.Profiles.CountAsync(x => x.HostId == hostId, ct) <= 1)
        {
            return new GuessingOperationResult(false, "Keep at least one round type.");
        }

        if (await db.Rounds.AnyAsync(x => x.GuessRoundProfileId == profileId, ct))
        {
            return new GuessingOperationResult(
                false,
                "Round types used by past rounds cannot be deleted."
            );
        }

        var wasDefault = profile.IsDefault;
        var deliverySettings = await db
            .ReplyDeliverySettings.Where(x =>
                x.HostId == hostId
                && x.Feature == ReplyDeliveryFeature.Guessing
                && x.ScopeId == profileId
            )
            .ToListAsync(ct);
        db.ReplyDeliverySettings.RemoveRange(deliverySettings);
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

        await changes.NotifyChangedAsync(ct);
        return new GuessingOperationResult(true, $"Deleted {profile.Name}.");
    }

    public async Task<GuessingConfiguration> LoadConfigurationAsync(
        int hostId,
        int? profileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var profiles = await LoadProfileSummariesAsync(db, hostId, ct);
        var selectedProfileId =
            profileId is { } id && profiles.Any(x => x.Id == id)
                ? id
                : profiles.First(x => x.IsDefault).Id;
        var aliases = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && x.GuessRoundProfileId == selectedProfileId)
            .ToListAsync(ct);
        var replyDelivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyDeliveryFeature.Guessing,
            selectedProfileId,
            ct
        );
        var whisperResponsesEnabled = await WhisperResponsesEnabledAsync(db, hostId, ct);

        return new GuessingConfiguration
        {
            Aliases = new CommandAliasEditor
            {
                StartAliases = JoinAliases(aliases, GuessCommandKind.Start, selectedProfileId),
                StopAliases = JoinAliases(aliases, GuessCommandKind.Stop, selectedProfileId),
                WinAliases = JoinAliases(aliases, GuessCommandKind.Win, selectedProfileId),
                GuessAliases = JoinAliases(aliases, GuessCommandKind.Guess, selectedProfileId),
                GuessesAliases = JoinAliases(aliases, GuessCommandKind.Guesses, selectedProfileId),
            },
            ReplyDelivery = replyDelivery,
            WhisperResponsesEnabled = whisperResponsesEnabled,
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

        var profile = await db
            .Profiles.Include(x => x.ReplySettings)
            .Include(x => x.Options)
            .SingleAsync(x => x.Id == config.Profile.Id && x.HostId == hostId, ct);

        await SaveAliasesAsync(db, hostId, profile.Id, config.Aliases, ct);

        var profileName = NormalizeDisplayName(config.Profile.Name);
        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName = profile.Name;
        }

        var slug = GuessRoundProfileSlug.FromName(profileName);
        var duplicate = await db.Profiles.AnyAsync(
            x => x.HostId == hostId && x.Id != profile.Id && x.Slug == slug.Value,
            ct
        );
        if (duplicate)
        {
            throw new InvalidOperationException("A round type with that name already exists.");
        }

        profile.Name = profileName;
        profile.Slug = slug.Value;
        profile.IsDefault = config.Profile.IsDefault;
        profile.WinningGuessPointReward = PointAmount
            .ParseAbsolute(config.Profile.WinningGuessPointReward)
            .ToString();

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

        profile.ReplySettings ??= ReplySettingsMapper.ToEntity(GuessingDefaults.Replies());
        Apply(profile.ReplySettings, config.Profile.Replies);
        await ReplyDeliverySettingWriter.ReplaceAsync(
            db,
            hostId,
            ReplyDeliveryFeature.Guessing,
            profile.Id,
            config.ReplyDelivery.Only(GuessingReplyKeys.WhisperableKeys),
            ct
        );

        db.GuessOptions.RemoveRange(profile.Options);
        var answerReplyTarget = ReplyDeliveryTargets.FromCommandTarget(
            config.Profile.WhisperAnswerReplies
                ? TwitchCommandResponseTarget.Whisper
                : TwitchCommandResponseTarget.Chat
        );
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
                    ReplyTarget = answerReplyTarget,
                }
            );
        }

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
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

    private static string JoinAliases(
        List<CommandAlias> aliases,
        GuessCommandKind kind,
        int profileId
    )
    {
        return CommandAliasRegistry.JoinAliases(
            aliases,
            GuessingAppCommandKindMap.ToAppKind(kind),
            profileId
        );
    }

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
            ?? throw new InvalidOperationException("Round type not found.");

        var options = profile
            .Options.OrderBy(x => x.Name)
            .Select(x => new GuessOptionEditor
            {
                Name = x.Name,
                ReplyText = x.ReplyText,
                ReplyTarget = ReplyDeliveryTargets.FromCommandTarget(
                    ReplyDeliveryTargets.ToCommandTarget(x.ReplyTarget)
                ),
            })
            .ToList();
        var whisperAnswerReplies = options.Any(IsWhisperTarget);
        var answerReplyTarget = ReplyDeliveryTargets.FromCommandTarget(
            whisperAnswerReplies
                ? TwitchCommandResponseTarget.Whisper
                : TwitchCommandResponseTarget.Chat
        );
        foreach (var option in options)
        {
            option.ReplyTarget = answerReplyTarget;
        }

        return new GuessRoundProfileEditor
        {
            Id = profile.Id,
            Name = profile.Name,
            IsDefault = profile.IsDefault,
            WhisperAnswerReplies = whisperAnswerReplies,
            WinningGuessPointReward = profile.WinningGuessPointReward,
            Replies = ReplySettingsMapper.ToEditor(
                profile.ReplySettings ?? ReplySettingsMapper.ToEntity(GuessingDefaults.Replies())
            ),
            Options = options,
        };
    }

    private static bool IsWhisperTarget(GuessOptionEditor option)
    {
        return ReplyDeliveryTargets.ToCommandTarget(option.ReplyTarget)
            == TwitchCommandResponseTarget.Whisper;
    }

    private static async Task<bool> WhisperResponsesEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        return await db
            .HostBotAccountSettings.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .Select(x => x.OverrideEnabled && x.WhisperResponsesEnabled)
            .SingleOrDefaultAsync(ct);
    }

    private async Task<List<GuessRoundProfileSummary>> LoadProfileSummariesAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        return await db
            .Profiles.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new GuessRoundProfileSummary(x.Id, x.Name, x.IsDefault))
            .ToListAsync(ct);
    }

    private static string NormalizeDisplayName(string name)
    {
        return name.Trim();
    }

    private async Task SaveAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        int profileId,
        CommandAliasEditor aliases,
        CancellationToken ct
    )
    {
        await aliasRegistry.ReplaceAliasesAsync(
            db,
            hostId,
            GuessingAppCommandKindMap.AppKinds,
            [
                new CommandAliasDraft(AppCommandKind.Start, aliases.StartAliases),
                new CommandAliasDraft(AppCommandKind.Stop, aliases.StopAliases),
                new CommandAliasDraft(AppCommandKind.Win, aliases.WinAliases),
                new CommandAliasDraft(AppCommandKind.Guess, aliases.GuessAliases),
                new CommandAliasDraft(AppCommandKind.Guesses, aliases.GuessesAliases),
            ],
            ct,
            profileId
        );
    }
}
