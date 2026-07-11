namespace BlokeBot.Features.HostedChannels.Authorization;

public interface IBotAccountAuthorizationPolicy
{
    Task<BotAccountAuthorizationStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

internal delegate Task<TwitchTokenStatus> BotAccountTokenStatusResolver(
    IEnumerable<string?> requiredScopes,
    CancellationToken cancellationToken
);
