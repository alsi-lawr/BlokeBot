using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.PublicLeaderboards;

public sealed record PublicLeaderboardHost(
    int Id,
    string Login,
    string DisplayName,
    HostFeatureFlags EnabledFeatures
);
