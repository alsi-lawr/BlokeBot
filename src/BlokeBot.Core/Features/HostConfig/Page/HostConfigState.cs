using BlokeBot.Core.Features.Commands;
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
    TwitchOperationsAuthorizationState TwitchOperationsAuthorization,
    HostedChannelRuntimeSummary? RuntimeStatus,
    StartupMessageConfiguration StartupMessage,
    CommandsConfiguration Commands,
    HostBotAccountOverrideState BotOverride,
    IReadOnlyList<HostFeatureCardState> Features,
    HostModAccessState ModAccess
);

public enum TwitchOperationsAuthorizationState
{
    Missing,
    Stale,
    Ready,
}

public sealed record HostBotAccountOverrideState(
    bool Enabled,
    BotAccountAuthorizationStatus Status,
    bool WhisperResponsesEnabled,
    WhisperQuotaStatus WhisperQuota
);
