using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.HostedChannels;

public sealed record HostFeatureCardState(
    HostFeatureFlags Feature,
    string Name,
    string Description,
    bool Enabled
);
