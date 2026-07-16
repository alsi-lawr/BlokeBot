using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Core.Features.Guessing.Replies;

namespace BlokeBot.Core.Features.Guessing.Configuration;

internal sealed class GuessingConfigurationDraftSnapshot
{
    private readonly GuessingCommandAliases _aliases;
    private readonly bool _isDefault;
    private readonly GuessOptionValue[] _options;
    private readonly int _profileId;
    private readonly string _profileName;
    private readonly long _profileRevision;
    private readonly GuessingReplySettings _replies;
    private readonly string _winningGuessPointReward;
    private readonly bool _whisperAnswerReplies;
    private readonly string[] _whisperReplyKeys;

    private GuessingConfigurationDraftSnapshot(GuessingConfiguration configuration)
    {
        var profile = configuration.Profile;
        _profileId = profile.Id;
        _profileRevision = profile.Revision;
        _profileName = profile.Name;
        _isDefault = profile.IsDefault;
        _whisperAnswerReplies = profile.WhisperAnswerReplies;
        _winningGuessPointReward = profile.WinningGuessPointReward;
        _aliases = CaptureAliases(configuration.Aliases);
        _replies = CaptureReplies(profile.Replies);
        _options = CaptureOptions(profile.Options);
        _whisperReplyKeys = CaptureWhisperReplyKeys(configuration);
    }

    internal static GuessingConfigurationDraftSnapshot Capture(GuessingConfiguration configuration)
    {
        return new(configuration);
    }

    internal bool Matches(GuessingConfiguration configuration)
    {
        var current = new GuessingConfigurationDraftSnapshot(configuration);
        return _profileId == current._profileId
            && _profileRevision == current._profileRevision
            && string.Equals(_profileName, current._profileName, StringComparison.Ordinal)
            && _isDefault == current._isDefault
            && _whisperAnswerReplies == current._whisperAnswerReplies
            && string.Equals(
                _winningGuessPointReward,
                current._winningGuessPointReward,
                StringComparison.Ordinal
            )
            && _aliases == current._aliases
            && _replies == current._replies
            && _options.SequenceEqual(current._options)
            && _whisperReplyKeys.SequenceEqual(current._whisperReplyKeys);
    }

    private static GuessingCommandAliases CaptureAliases(CommandAliasEditor aliases)
    {
        return new(
            aliases.StartAliases,
            aliases.StopAliases,
            aliases.WinAliases,
            aliases.GuessAliases,
            aliases.GuessesAliases
        );
    }

    private static GuessingReplySettings CaptureReplies(ReplySettingsEditor replies)
    {
        return new(
            replies.RoundStartedReply,
            replies.RoundAlreadyOpenReply,
            replies.NoOpenRoundReply,
            replies.GuessingStoppedReply,
            replies.GuessingAlreadyStoppedReply,
            replies.GuessingClosedReply,
            replies.InvalidGuessReply,
            replies.GuessUsageReply,
            replies.AvailableGuessesReply,
            replies.WinUsageReply,
            replies.ModeratorOnlyReply,
            replies.WinnerReply,
            replies.NoWinnersReply
        );
    }

    private static GuessOptionValue[] CaptureOptions(IEnumerable<GuessOptionEditor> options)
    {
        return options
            .Select(option => new GuessOptionValue(
                option.Name,
                option.ReplyText,
                option.ReplyTarget
            ))
            .ToArray();
    }

    private static string[] CaptureWhisperReplyKeys(GuessingConfiguration configuration)
    {
        return configuration
            .ReplyDelivery.ToMap()
            .WhisperKeys.OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }
}
