namespace BlokeBot.Core.Features.HostedChannels.Status;

public interface IHostBotAppAccessTokenSource
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
