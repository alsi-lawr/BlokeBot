using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChannelToolEnablementV1(
    [property: JsonRequired] bool Automations,
    [property: JsonRequired] bool Polls,
    [property: JsonRequired] bool ClipsAndMarkers,
    [property: JsonRequired] bool RewardsAndRedemptions,
    [property: JsonRequired] bool Predictions,
    [property: JsonRequired] bool RequestBoards,
    [property: JsonRequired] bool PlayWithViewers,
    [property: JsonRequired] bool Moments,
    [property: JsonRequired] bool Overlays,
    [property: JsonRequired] bool Guessing,
    [property: JsonRequired] bool Points,
    [property: JsonRequired] bool Bounties,
    [property: JsonRequired] bool CommunityProgression,
    [property: JsonRequired] bool CooperativeGame,
    [property: JsonRequired] bool ViewerPassports,
    [property: JsonRequired] bool Bingo,
    [property: JsonRequired] bool Competitions,
    [property: JsonRequired] bool RaidCollaboration,
    [property: JsonRequired] bool Collectives,
    [property: JsonRequired] bool CustomCommands
);
