using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Configuration;

public sealed partial class GuessingConfigurationService
{
    private async ValueTask<
        Result<GuessingConfiguration, GuessingConfigurationLoadFailure>
    > ExecuteLoadConfigurationAsync(
        int hostId,
        GuessingProfileSelection selection,
        CancellationToken ct
    )
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
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
        var pinPolicy = await db
            .ReplyPinPolicies.AsNoTracking()
            .SingleOrDefaultAsync(
                policy =>
                    policy.HostId == hostId
                    && policy.Feature == "guessing"
                    && policy.ReplyKey == GuessingReplyKeys.RoundStarted,
                ct
            );
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
            new GuessingPinEditor
            {
                Enabled = pinPolicy is not null,
                DurationSeconds = pinPolicy?.DurationSeconds,
                UnpinWhenRoundStops = pinPolicy?.UnpinOnOwnerCompletion ?? false,
            },
            whisperResponsesEnabled,
            profiles,
            profile
        );
        return Result<GuessingConfiguration, GuessingConfigurationLoadFailure>.Success(draft);
    }

    private static string JoinAliases(
        List<CommandAlias> aliases,
        GuessCommandKind kind,
        int profileId
    ) =>
        CommandAliasRegistry.JoinAliases(
            aliases,
            GuessingAppCommandKindMap.ToAppKind(kind),
            new CommandAliasScope.Profile(profileId)
        );

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
    ) =>
        await db
            .HostBotAccountSettings.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .Select(x => x.OverrideEnabled && x.WhisperResponsesEnabled)
            .SingleOrDefaultAsync(ct);

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
