using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Guessing.Configuration;

internal static class GuessingConfigurationGraphStager
{
    public static async Task<GuessingConfigurationSaveFailure?> FindAliasFailureAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessingConfigurationSaveCommand command,
        CancellationToken cancellationToken
    ) =>
        await GuessingAliasCollisionValidator.FindForSaveAsync(
            db,
            hostId,
            command,
            cancellationToken
        );

    public static void ApplyProfile(
        BlokeBotDbContext db,
        int hostId,
        GuessRoundProfile profile,
        GuessingConfigurationSaveCommand command
    )
    {
        profile.Name = command.ProfileName;
        profile.Slug = GuessRoundProfileSlug.FromName(command.ProfileName).Value;
        profile.IsDefault = command.IsDefault || profile.IsDefault;
        profile.WinningGuessPointReward = command.WinningGuessPointReward.ToString();
        profile.ReplySettings ??= ReplySettingsMapper.ToEntity(GuessingDefaults.Replies());
        ApplyReplies(profile.ReplySettings, command.Replies);
        ReplaceAliases(db, hostId, profile, command);

        db.GuessOptions.RemoveRange(profile.Options);
        profile.Options = command
            .Options.Select(
                (option, index) =>
                    new GuessOption
                    {
                        GuessRoundProfile = profile,
                        Name = option.Name,
                        ReplyText = option.ReplyText,
                        ReplyTarget = option.ReplyTarget,
                        SortOrder = index,
                    }
            )
            .ToList();
    }

    private static void ReplaceAliases(
        BlokeBotDbContext db,
        int hostId,
        GuessRoundProfile profile,
        GuessingConfigurationSaveCommand command
    )
    {
        if (profile.Id > 0)
        {
            var ownedKinds = GuessingAppCommandKindMap.AppKinds.ToArray();
            db.CommandAliases.RemoveRange(
                db.CommandAliases.Where(alias =>
                    alias.HostId == hostId
                    && ownedKinds.Contains(alias.Kind)
                    && alias.GuessRoundProfileId == profile.Id
                )
            );
        }
        profile.CommandAliases = command
            .Aliases.ToDrafts()
            .SelectMany(draft =>
                BlokeBot
                    .Commands.CommandAliasNormalizer.Split(draft.Aliases)
                    .Select(alias => new CommandAlias
                    {
                        HostId = hostId,
                        GuessRoundProfile = profile,
                        Kind = draft.Kind,
                        Alias = alias,
                    })
            )
            .ToList();
    }

    private static void ApplyReplies(BotReplySettings settings, GuessingReplySettings replies)
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
}
