using BlokeBot.Features.HostConfig.Access;
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
    HostModAccessState ModAccess
);
