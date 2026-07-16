namespace BlokeBot.Core.Features.HostedChannels.Authorization;

public interface IHostBotAccountTokenStatusProvider
{
    Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
        string channelLogin,
        IEnumerable<string?> requiredScopes,
        CancellationToken cancellationToken
    );
}
