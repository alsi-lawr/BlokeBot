using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Whispers;

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
    HostBotAccountOverrideState BotOverride,
    IReadOnlyList<HostFeatureCardState> Features,
    HostModAccessState ModAccess
);

public sealed record HostBotAccountOverrideState(
    bool Enabled,
    BotAccountAuthorizationStatus Status,
    bool WhisperResponsesEnabled,
    WhisperQuotaStatus WhisperQuota
);
