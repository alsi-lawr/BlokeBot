namespace BlokeBot.Features.HostedChannels;

using BlokeBot.Persistence.Models;

public sealed record HostFeatureCardState(
    HostFeatureFlags Feature,
    string Name,
    string Description,
    bool Enabled
);
