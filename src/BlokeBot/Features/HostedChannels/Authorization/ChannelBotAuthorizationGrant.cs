using BlokeBot.Identity;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed record ChannelBotAuthorizationGrant(
    string UserId,
    LoginName Login,
    OAuthScopeSet Scopes
);
