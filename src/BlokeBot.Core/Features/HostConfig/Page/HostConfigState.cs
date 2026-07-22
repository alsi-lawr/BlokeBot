using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.StartupMessage;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Whispers;

namespace BlokeBot.Core.Features.HostConfig.Page;

public sealed record HostConfigState(
    int? HostId,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    bool CanCreateHost,
    bool IsHostCreated,
    bool IsChannelBotAuthorized,
    HostedChannelRuntimeSummary? RuntimeStatus,
    StartupMessageConfiguration StartupMessage,
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
