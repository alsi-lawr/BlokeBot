using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

public sealed record HostBotAccountAuthorizationGrant(
    HostBotAccountTokenPayload Token,
    string UserId,
    LoginName Login,
    string DisplayName,
    string? ProfileImageUrl,
    OAuthScopeSet Scopes
)
{
    public override string ToString() =>
        $"{nameof(HostBotAccountAuthorizationGrant)} {{ Token = [REDACTED] }}";
}
