namespace BlokeBot.Core.Features.HostedChannels.Status;

internal sealed class OAuthHostBotAppAccessTokenSource(AppAccessTokenProvider appTokens)
    : IHostBotAppAccessTokenSource
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        appTokens.GetAccessTokenAsync(cancellationToken);
}
