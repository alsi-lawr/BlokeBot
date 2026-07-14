using BlokeBot.Features.Commands;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Guessing.Configuration;

public sealed record GuessingCommandAliases(
    string StartAliases,
    string StopAliases,
    string WinAliases,
    string GuessAliases,
    string GuessesAliases
)
{
    internal IReadOnlyList<CommandAliasDraft> ToDrafts()
    {
        return
        [
            new(AppCommandKind.Start, StartAliases),
            new(AppCommandKind.Stop, StopAliases),
            new(AppCommandKind.Win, WinAliases),
            new(AppCommandKind.Guess, GuessAliases),
            new(AppCommandKind.Guesses, GuessesAliases),
        ];
    }
}

public sealed record GuessingReplySettings(
    string RoundStartedReply,
    string RoundAlreadyOpenReply,
    string NoOpenRoundReply,
    string GuessingStoppedReply,
    string GuessingAlreadyStoppedReply,
    string GuessingClosedReply,
    string InvalidGuessReply,
    string GuessUsageReply,
    string AvailableGuessesReply,
    string WinUsageReply,
    string ModeratorOnlyReply,
    string WinnerReply,
    string NoWinnersReply
);

public sealed record GuessOptionValue(
    string Name,
    string ReplyText,
    ReplyDeliveryTarget ReplyTarget
);

public sealed record GuessingConfigurationSaveCommand
{
    internal GuessingConfigurationSaveCommand(
        int profileId,
        long expectedRevision,
        string profileName,
        bool isDefault,
        PointAmount winningGuessPointReward,
        GuessingCommandAliases aliases,
        GuessingReplySettings replies,
        ReplyDeliveryMap replyDelivery,
        IEnumerable<GuessOptionValue> options
    )
    {
        ProfileId = profileId;
        ExpectedRevision = expectedRevision;
        ProfileName = profileName;
        IsDefault = isDefault;
        WinningGuessPointReward = winningGuessPointReward;
        Aliases = aliases;
        Replies = replies;
        ReplyDelivery = ReplyDeliveryMap.FromWhisperKeys(replyDelivery.WhisperKeys);
        Options = Array.AsReadOnly(options.ToArray());
    }

    public int ProfileId { get; }

    public long ExpectedRevision { get; }

    public string ProfileName { get; }

    public bool IsDefault { get; }

    public PointAmount WinningGuessPointReward { get; }

    public GuessingCommandAliases Aliases { get; }

    public GuessingReplySettings Replies { get; }

    public ReplyDeliveryMap ReplyDelivery { get; }

    public IReadOnlyList<GuessOptionValue> Options { get; }
}

public sealed record GuessingProfileCreateCommand
{
    internal GuessingProfileCreateCommand(string name, string slug)
    {
        Name = name;
        Slug = slug;
    }

    public string Name { get; }

    internal string Slug { get; }
}

public sealed record GuessingProfileDeleteCommand
{
    internal GuessingProfileDeleteCommand(int profileId, long expectedRevision)
    {
        ProfileId = profileId;
        ExpectedRevision = expectedRevision;
    }

    public int ProfileId { get; }

    public long ExpectedRevision { get; }
}
