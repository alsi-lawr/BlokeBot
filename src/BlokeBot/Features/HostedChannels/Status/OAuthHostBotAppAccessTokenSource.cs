namespace BlokeBot.Features.HostedChannels.Status;

internal sealed class OAuthHostBotAppAccessTokenSource(AppAccessTokenProvider appTokens)
    : IHostBotAppAccessTokenSource
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        return appTokens.GetAccessTokenAsync(cancellationToken);
    }
}
