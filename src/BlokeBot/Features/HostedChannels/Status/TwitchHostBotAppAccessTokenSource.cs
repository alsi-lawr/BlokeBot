namespace BlokeBot.Features.HostedChannels.Status;

internal sealed class TwitchHostBotAppAccessTokenSource(
    TwitchAppAccessTokenProvider appTokens
) : IHostBotAppAccessTokenSource
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        appTokens.GetAccessTokenAsync(cancellationToken);
}
