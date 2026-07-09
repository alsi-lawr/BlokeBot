using BlokeBot.Identity;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed record HostBotAccountAuthorizationGrant(
    TwitchTokenSet Token,
    string UserId,
    LoginName Login,
    string DisplayName,
    string? ProfileImageUrl,
    IReadOnlyList<string> Scopes
);
