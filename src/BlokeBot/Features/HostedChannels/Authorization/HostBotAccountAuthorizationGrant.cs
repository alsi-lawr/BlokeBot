using BlokeBot.Identity;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed record HostBotAccountAuthorizationGrant(
    TokenSet Token,
    string UserId,
    LoginName Login,
    string DisplayName,
    string? ProfileImageUrl,
    OAuthScopeSet Scopes
);
