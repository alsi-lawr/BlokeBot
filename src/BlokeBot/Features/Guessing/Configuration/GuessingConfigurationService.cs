using System.Data;
using BlokeBot.Features.Commands;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Configuration;

public sealed class GuessingConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    GuessingChangeNotifier changes
)
{
    public IO<GuessingProfileCreated, GuessingProfileCreateFailure> CreateProfile(
        int hostId,
        GuessingProfileCreateCommand command
    )
    {
        return IO<GuessingProfileCreated, GuessingProfileCreateFailure>.Create(ct =>
            ExecuteCreateProfileAsync(hostId, command, ct)
        );
    }

    public IO<GuessingProfileDeleted, GuessingProfileDeleteFailure> DeleteProfile(
        int hostId,
        GuessingProfileDeleteCommand command
    )
    {
        return IO<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Create(ct =>
            ExecuteDeleteProfileAsync(hostId, command, ct)
        );
    }

    public IO<GuessingConfiguration, GuessingConfigurationLoadFailure> LoadConfiguration(
        int hostId,
        GuessingProfileSelection selection
    )
    {
        return IO<GuessingConfiguration, GuessingConfigurationLoadFailure>.Create(ct =>
            ExecuteLoadConfigurationAsync(hostId, selection, ct)
        );
    }

    public IO<GuessingConfigurationSaved, GuessingConfigurationSaveFailure> SaveConfiguration(
        int hostId,
        GuessingConfigurationSaveCommand command
    )
    {
        return IO<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Create(ct =>
            ExecuteSaveConfigurationAsync(hostId, command, ct)
        );
    }

    private async ValueTask<
        Result<GuessingProfileCreated, GuessingProfileCreateFailure>
    > ExecuteCreateProfileAsync(
        int hostId,
        GuessingProfileCreateCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct
        );
        if (await db.Profiles.AnyAsync(x => x.HostId == hostId && x.Slug == command.Slug, ct))
        {
            return Result<GuessingProfileCreated, GuessingProfileCreateFailure>.Error(
                new GuessingProfileCreateFailure()
            );
        }

        var profile = new GuessRoundProfile
        {
            Name = command.Name,
            Slug = command.Slug,
            HostId = hostId,
            IsDefault = !await db.Profiles.AnyAsync(x => x.HostId == hostId, ct),
            ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return Result<GuessingProfileCreated, GuessingProfileCreateFailure>.Success(
            new(profile.Id, $"Created {profile.Name}.")
        );
    }

    private async ValueTask<
        Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>
    > ExecuteDeleteProfileAsync(
        int hostId,
        GuessingProfileDeleteCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct
        );
        var claimed = await db
            .Profiles.Where(profile =>
                profile.HostId == hostId
                && profile.Id == command.ProfileId
                && profile.Revision == command.ExpectedRevision
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(
                        profile => profile.Revision,
                        profile => profile.Revision + 1
                    ),
                ct
            );
        if (claimed == 0)
        {
            var exists = await db.Profiles.AnyAsync(
                profile => profile.HostId == hostId && profile.Id == command.ProfileId,
                ct
            );
            return Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Error(
                exists
                    ? new GuessingProfileDeleteFailure.ConcurrentEdit()
                    : new GuessingProfileDeleteFailure.ProfileNotFound()
            );
        }

        var profile = await db.Profiles.SingleAsync(
            x => x.Id == command.ProfileId && x.HostId == hostId,
            ct
        );
        if (await db.Profiles.CountAsync(x => x.HostId == hostId, ct) <= 1)
        {
            return Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Error(
                new GuessingProfileDeleteFailure.LastProfile()
            );
        }

        if (await db.Rounds.AnyAsync(x => x.GuessRoundProfileId == profile.Id, ct))
        {
            return Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Error(
                new GuessingProfileDeleteFailure.UsedByPastRound()
            );
        }

        if (profile.IsDefault)
        {
            profile.IsDefault = false;
            await db.SaveChangesAsync(ct);
            var nextDefault = await db
                .Profiles.Where(x => x.HostId == hostId && x.Id != profile.Id)
                .OrderBy(x => x.Name)
                .FirstAsync(ct);
            nextDefault.IsDefault = true;
            nextDefault.Revision++;
        }

        var deliverySettings = await db
            .ReplyDeliverySettings.Where(x =>
                x.HostId == hostId && x.Feature == ReplyFeature.Guessing && x.ScopeId == profile.Id
            )
            .ToListAsync(ct);
        db.ReplyDeliverySettings.RemoveRange(deliverySettings);
        db.Profiles.Remove(profile);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return Result<GuessingProfileDeleted, GuessingProfileDeleteFailure>.Success(
            new($"Deleted {profile.Name}.")
        );
    }

    private async ValueTask<
        Result<GuessingConfiguration, GuessingConfigurationLoadFailure>
    > ExecuteLoadConfigurationAsync(
        int hostId,
        GuessingProfileSelection selection,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profiles = await LoadProfileSummariesAsync(db, hostId, ct);
        int? selectedProfileId = selection switch
        {
            GuessingProfileSelection.Default => profiles
                .SingleOrDefault(profile => profile.IsDefault)
                ?.Id,
            GuessingProfileSelection.Selected selected
                when profiles.Any(profile => profile.Id == selected.ProfileId) =>
                selected.ProfileId,
            GuessingProfileSelection.Selected => null,
            _ => throw new InvalidOperationException("Unknown guessing profile selection."),
        };
        if (selectedProfileId is not { } profileId)
        {
            return Result<GuessingConfiguration, GuessingConfigurationLoadFailure>.Error(
                new GuessingConfigurationLoadFailure()
            );
        }

        var profile = await LoadProfileEditorAsync(db, hostId, profileId, ct);
        if (profile is null)
        {
            return Result<GuessingConfiguration, GuessingConfigurationLoadFailure>.Error(
                new GuessingConfigurationLoadFailure()
            );
        }

        var aliases = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && x.GuessRoundProfileId == profileId)
            .ToListAsync(ct);
        var replyDelivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyFeature.Guessing,
            profileId,
            ct
        );
        var whisperResponsesEnabled = await WhisperResponsesEnabledAsync(db, hostId, ct);
        var draft = new GuessingConfiguration(
            new CommandAliasEditor
            {
                StartAliases = JoinAliases(aliases, GuessCommandKind.Start, profileId),
                StopAliases = JoinAliases(aliases, GuessCommandKind.Stop, profileId),
                WinAliases = JoinAliases(aliases, GuessCommandKind.Win, profileId),
                GuessAliases = JoinAliases(aliases, GuessCommandKind.Guess, profileId),
                GuessesAliases = JoinAliases(aliases, GuessCommandKind.Guesses, profileId),
            },
            ReplyDeliveryEditor.From(replyDelivery),
            whisperResponsesEnabled,
            profiles,
            profile
        );
        return Result<GuessingConfiguration, GuessingConfigurationLoadFailure>.Success(draft);
    }

    private async ValueTask<
        Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>
    > ExecuteSaveConfigurationAsync(
        int hostId,
        GuessingConfigurationSaveCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct
        );
        var claimed = await db
            .Profiles.Where(profile =>
                profile.HostId == hostId
                && profile.Id == command.ProfileId
                && profile.Revision == command.ExpectedRevision
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(
                        profile => profile.Revision,
                        profile => profile.Revision + 1
                    ),
                ct
            );
        if (claimed == 0)
        {
            var exists = await db.Profiles.AnyAsync(
                profile => profile.HostId == hostId && profile.Id == command.ProfileId,
                ct
            );
            return Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Error(
                exists
                    ? new GuessingConfigurationSaveFailure.ConcurrentEdit()
                    : new GuessingConfigurationSaveFailure.ProfileNotFound()
            );
        }

        var slug = GuessRoundProfileSlug.FromName(command.ProfileName).Value;
        if (
            await db.Profiles.AnyAsync(
                profile =>
                    profile.HostId == hostId
                    && profile.Id != command.ProfileId
                    && profile.Slug == slug,
                ct
            )
        )
        {
            return Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Error(
                new GuessingConfigurationSaveFailure.DuplicateProfileName()
            );
        }

        var aliasFailure = await FindAliasFailureAsync(db, hostId, command, ct);
        if (aliasFailure is not null)
        {
            return Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Error(
                aliasFailure
            );
        }

        if (command.IsDefault)
        {
            await db
                .Profiles.Where(profile =>
                    profile.HostId == hostId && profile.Id != command.ProfileId && profile.IsDefault
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(profile => profile.IsDefault, false)
                            .SetProperty(
                                profile => profile.Revision,
                                profile => profile.Revision + 1
                            ),
                    ct
                );
        }

        var profile = await db
            .Profiles.Include(x => x.ReplySettings)
            .Include(x => x.Options)
            .SingleAsync(x => x.Id == command.ProfileId && x.HostId == hostId, ct);
        profile.Name = command.ProfileName;
        profile.Slug = slug;
        profile.IsDefault = command.IsDefault || profile.IsDefault;
        profile.WinningGuessPointReward = command.WinningGuessPointReward.ToString();
        profile.ReplySettings ??= ReplySettingsMapper.ToEntity(GuessingDefaults.Replies());
        Apply(profile.ReplySettings, command.Replies);
        ReplaceAliases(db, hostId, command);
        await ReplyDeliverySettingWriter.ReplaceAsync(
            db,
            hostId,
            ReplyFeature.Guessing,
            profile.Id,
            command.ReplyDelivery.Only(GuessingReplyKeys.WhisperableKeys),
            ct
        );

        db.GuessOptions.RemoveRange(profile.Options);
        for (var index = 0; index < command.Options.Count; index++)
        {
            var option = command.Options[index];
            db.GuessOptions.Add(
                new GuessOption
                {
                    GuessRoundProfile = profile,
                    Name = option.Name,
                    ReplyText = option.ReplyText,
                    ReplyTarget = option.ReplyTarget,
                    SortOrder = index,
                }
            );
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return Result<GuessingConfigurationSaved, GuessingConfigurationSaveFailure>.Success(new());
    }

    private static async Task<GuessingConfigurationSaveFailure?> FindAliasFailureAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessingConfigurationSaveCommand command,
        CancellationToken ct
    )
    {
        var requestedAliases = command
            .Aliases.ToDrafts()
            .SelectMany(draft => CommandAliasNormalizer.Split(draft.Aliases))
            .ToArray();
        var ownedKinds = GuessingAppCommandKindMap.AppKinds.ToArray();
        var collision = await db
            .CommandAliases.AsNoTracking()
            .Where(alias => requestedAliases.Contains(alias.Alias))
            .Where(alias =>
                alias.HostId == hostId
                && (
                    !ownedKinds.Contains(alias.Kind)
                    || alias.GuessRoundProfileId != command.ProfileId
                )
            )
            .Select(alias => alias.Alias)
            .FirstOrDefaultAsync(ct);
        return collision is null
            ? null
            : new GuessingConfigurationSaveFailure.AliasAlreadyUsed(collision);
    }

    private static void ReplaceAliases(
        BlokeBotDbContext db,
        int hostId,
        GuessingConfigurationSaveCommand command
    )
    {
        var ownedKinds = GuessingAppCommandKindMap.AppKinds.ToArray();
        db.CommandAliases.RemoveRange(
            db.CommandAliases.Where(alias =>
                alias.HostId == hostId
                && ownedKinds.Contains(alias.Kind)
                && alias.GuessRoundProfileId == command.ProfileId
            )
        );
        db.CommandAliases.AddRange(
            command
                .Aliases.ToDrafts()
                .SelectMany(draft =>
                    CommandAliasNormalizer
                        .Split(draft.Aliases)
                        .Select(alias => new CommandAlias
                        {
                            HostId = hostId,
                            GuessRoundProfileId = command.ProfileId,
                            Kind = draft.Kind,
                            Alias = alias,
                        })
                )
        );
    }

    private static void Apply(BotReplySettings settings, GuessingReplySettings replies)
    {
        settings.RoundStartedReply = replies.RoundStartedReply;
        settings.RoundAlreadyOpenReply = replies.RoundAlreadyOpenReply;
        settings.NoOpenRoundReply = replies.NoOpenRoundReply;
        settings.GuessingStoppedReply = replies.GuessingStoppedReply;
        settings.GuessingAlreadyStoppedReply = replies.GuessingAlreadyStoppedReply;
        settings.GuessingClosedReply = replies.GuessingClosedReply;
        settings.InvalidGuessReply = replies.InvalidGuessReply;
        settings.GuessUsageReply = replies.GuessUsageReply;
        settings.AvailableGuessesReply = replies.AvailableGuessesReply;
        settings.WinUsageReply = replies.WinUsageReply;
        settings.ModeratorOnlyReply = replies.ModeratorOnlyReply;
        settings.WinnerReply = replies.WinnerReply;
        settings.NoWinnersReply = replies.NoWinnersReply;
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
            new CommandAliasScope.Profile(profileId)
        );
    }

    internal static async Task<GuessRoundProfileEditor?> LoadProfileEditorAsync(
        BlokeBotDbContext db,
        int hostId,
        int profileId,
        CancellationToken ct
    )
    {
        var profile = await db
            .Profiles.AsNoTracking()
            .Include(x => x.ReplySettings)
            .Include(x => x.Options)
            .SingleOrDefaultAsync(x => x.Id == profileId && x.HostId == hostId, ct);
        if (profile is null)
        {
            return null;
        }
        var options = profile
            .Options.OrderBy(option => option.SortOrder)
            .ThenBy(option => option.Name)
            .Select(option => new GuessOptionEditor
            {
                Name = option.Name,
                ReplyText = option.ReplyText,
                ReplyTarget = option.ReplyTarget,
            })
            .ToList();
        var whisperAnswerReplies = options.Any(option => option.ReplyTarget.IsWhisper());
        var answerReplyTarget = whisperAnswerReplies
            ? ReplyDeliveryTarget.Whisper
            : ReplyDeliveryTarget.Chat;
        foreach (var option in options)
        {
            option.ReplyTarget = answerReplyTarget;
        }

        return new GuessRoundProfileEditor
        {
            Id = profile.Id,
            Revision = profile.Revision,
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

    private static async Task<IReadOnlyList<GuessRoundProfileSummary>> LoadProfileSummariesAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var profiles = await db
            .Profiles.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new GuessRoundProfileSummary(x.Id, x.Revision, x.Name, x.IsDefault))
            .ToArrayAsync(ct);
        return Array.AsReadOnly(profiles);
    }
}
