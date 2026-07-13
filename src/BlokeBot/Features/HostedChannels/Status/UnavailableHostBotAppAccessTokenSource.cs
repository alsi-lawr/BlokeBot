namespace BlokeBot.Features.HostedChannels.Status;

internal sealed class UnavailableHostBotAppAccessTokenSource : IHostBotAppAccessTokenSource
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        throw new HostBotAppAccessTokenUnavailableException();
    }
}

internal sealed class HostBotAppAccessTokenUnavailableException()
    : InvalidOperationException("The Twitch bot runner is not set up yet.");
