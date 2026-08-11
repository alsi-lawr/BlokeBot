namespace BlokeBot.Persistence.Models;

[Flags]
public enum HostFeatureFlags : ulong
{
    None = 0UL,
    Guessing = 1UL << 0,
    Points = 1UL << 1,
    CustomCommands = 1UL << 2,
    Shoutouts = 1UL << 3,
    Overlays = 1UL << 4,
    RequestBoards = 1UL << 5,
    PlayWithViewers = 1UL << 6,
    Moments = 1UL << 7,
    Polls = 1UL << 8,
    ClipsAndMarkers = 1UL << 9,
    RewardsAndRedemptions = 1UL << 10,
    Predictions = 1UL << 11,
    Automations = 1UL << 12,
    Bounties = 1UL << 13,
    CommunityProgression = 1UL << 14,
    Bingo = 1UL << 15,
    ViewerPassports = 1UL << 16,
    Competitions = 1UL << 17,
    CooperativeGame = 1UL << 18,
    NativeTwitchFeatures =
        Shoutouts | Polls | ClipsAndMarkers | RewardsAndRedemptions | Predictions,
    All =
        Guessing
        | Points
        | CustomCommands
        | Shoutouts
        | Overlays
        | RequestBoards
        | PlayWithViewers
        | Moments
        | Polls
        | ClipsAndMarkers
        | RewardsAndRedemptions
        | Predictions
        | Automations
        | Bounties
        | CommunityProgression
        | Bingo
        | ViewerPassports
        | Competitions
        | CooperativeGame,
}
