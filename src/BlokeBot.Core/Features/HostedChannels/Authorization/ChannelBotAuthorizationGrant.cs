using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

public sealed record ChannelBotAuthorizationGrant(
    string UserId,
    LoginName Login,
    OAuthScopeSet Scopes
);
