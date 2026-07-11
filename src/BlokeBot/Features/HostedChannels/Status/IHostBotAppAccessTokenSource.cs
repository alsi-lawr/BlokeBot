namespace BlokeBot.Features.HostedChannels.Status;

public interface IHostBotAppAccessTokenSource
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
