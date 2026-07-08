using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed record HostedChannelRuntimeStatus(
    bool IsChannelBotAuthorized,
    bool ChannelBotAuthorizationScopesCurrent,
    HostBotChannelStatus BotChannelStatus,
    BotChannelRuntimeState RuntimeState
);

public sealed record HostedChannelRuntimeSummary(
    bool IsChannelBotAuthorized,
    bool ChannelBotAuthorizationScopesCurrent,
    BotChannelRuntimeState RuntimeState
);
