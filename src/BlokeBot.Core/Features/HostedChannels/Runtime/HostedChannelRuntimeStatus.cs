using BlokeBot.Core.Features.HostedChannels.Status;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

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
