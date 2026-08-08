namespace BlokeBot.Core.Features.HostedChannels.Runtime;

public sealed record HostedChannelRuntimeSummary(
    bool IsChannelBotAuthorized,
    bool ChannelBotAuthorizationScopesCurrent,
    HostedChannelRuntimeLifecycle Lifecycle
);
