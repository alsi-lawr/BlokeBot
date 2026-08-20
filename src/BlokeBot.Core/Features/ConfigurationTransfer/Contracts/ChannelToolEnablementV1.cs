using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChannelToolEnablementV1(
    bool Automations,
    bool Polls,
    bool ClipsAndMarkers,
    bool RewardsAndRedemptions,
    bool Predictions,
    bool RequestBoards,
    bool PlayWithViewers,
    bool Moments,
    bool Overlays,
    bool Guessing,
    bool Points,
    bool Bounties,
    bool CommunityProgression,
    bool CooperativeGame,
    bool ViewerPassports,
    bool Bingo,
    bool Competitions,
    bool RaidCollaboration,
    bool Collectives,
    bool CustomCommands
);
