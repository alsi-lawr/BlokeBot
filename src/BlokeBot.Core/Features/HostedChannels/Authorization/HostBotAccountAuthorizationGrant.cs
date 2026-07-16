using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

public sealed record HostBotAccountAuthorizationGrant(
    TokenSet Token,
    string UserId,
    LoginName Login,
    string DisplayName,
    string? ProfileImageUrl,
    OAuthScopeSet Scopes
);
