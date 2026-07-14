using BlokeBot.Features.HostedChannels.Status;

namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed record HostedChannelRuntimeStatus(
    bool IsChannelBotAuthorized,
    bool ChannelBotAuthorizationScopesCurrent,
    HostBotChannelStatus BotChannelStatus,
    HostedChannelRuntimeLifecycle Lifecycle
);

public sealed record HostedChannelRuntimeSummary(
    bool IsChannelBotAuthorized,
    bool ChannelBotAuthorizationScopesCurrent,
    HostedChannelRuntimeLifecycle Lifecycle
);
