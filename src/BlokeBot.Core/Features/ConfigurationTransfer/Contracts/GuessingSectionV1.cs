using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GuessingSectionV1(IReadOnlyList<GuessingProfileV1> Profiles);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GuessingProfileV1(
    string Id,
    string Name,
    string Slug,
    bool IsDefault,
    string WinningGuessPointReward,
    IReadOnlyList<CommandAliasesV1> CommandAliases,
    GuessingRepliesV1 Replies,
    IReadOnlyList<GuessOptionV1> Options
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CommandAliasesV1(AppCommandKind Command, IReadOnlyList<string> Aliases);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GuessOptionV1(string Name, string ReplyText, ReplyDeliveryTarget ReplyTarget);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GuessingRepliesV1(
    string RoundStarted,
    string RoundAlreadyOpen,
    string NoOpenRound,
    string GuessingStopped,
    string GuessingAlreadyStopped,
    string GuessingClosed,
    string InvalidGuess,
    string GuessUsage,
    string AvailableGuesses,
    string WinUsage,
    string ModeratorOnly,
    string Winner,
    string NoWinners
);
