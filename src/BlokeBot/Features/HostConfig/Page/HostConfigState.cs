using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;

namespace BlokeBot.Features.HostConfig.Page;

public sealed record HostConfigState(
    int? HostId,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    bool CanCreateHost,
    bool IsHostCreated,
    bool IsChannelBotAuthorized,
    HostedChannelRuntimeSummary? RuntimeStatus,
    DateTime? LastRuntimeChangeAtUtc,
    HostBotAccountOverrideState BotOverride,
    IReadOnlyList<HostFeatureCardState> Features,
    HostModAccessState ModAccess
);

public sealed record HostBotAccountOverrideState(
    bool Enabled,
    BotAccountAuthorizationStatus Status
);
