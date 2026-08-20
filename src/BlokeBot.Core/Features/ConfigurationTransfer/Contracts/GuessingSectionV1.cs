using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GuessingSectionV1(
    [property: JsonRequired] IReadOnlyList<GuessingProfileV1> Profiles
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GuessingProfileV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Slug,
    [property: JsonRequired] bool IsDefault,
    [property: JsonRequired] string WinningGuessPointReward,
    [property: JsonRequired] IReadOnlyList<CommandAliasesV1> CommandAliases,
    [property: JsonRequired] GuessingRepliesV1 Replies,
    [property: JsonRequired] IReadOnlyList<GuessOptionV1> Options
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CommandAliasesV1(
    [property: JsonRequired] AppCommandKind Command,
    [property: JsonRequired] IReadOnlyList<string> Aliases
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GuessOptionV1(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string ReplyText,
    [property: JsonRequired] ReplyDeliveryTarget ReplyTarget
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GuessingRepliesV1(
    [property: JsonRequired] string RoundStarted,
    [property: JsonRequired] string RoundAlreadyOpen,
    [property: JsonRequired] string NoOpenRound,
    [property: JsonRequired] string GuessingStopped,
    [property: JsonRequired] string GuessingAlreadyStopped,
    [property: JsonRequired] string GuessingClosed,
    [property: JsonRequired] string InvalidGuess,
    [property: JsonRequired] string GuessUsage,
    [property: JsonRequired] string AvailableGuesses,
    [property: JsonRequired] string WinUsage,
    [property: JsonRequired] string ModeratorOnly,
    [property: JsonRequired] string Winner,
    [property: JsonRequired] string NoWinners
);
