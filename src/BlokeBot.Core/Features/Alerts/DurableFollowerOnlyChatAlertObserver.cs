using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Alerts;

internal sealed class DurableFollowerOnlyChatAlertObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    DurableAlertService alerts
) : IPublicChatTerminalRejectionObserver
{
    private const string _followersOnlyCode = "followers_only";
    private const string _source = "twitch-follower-only-chat";
    private const string _sourceKey = "followers_only";
    private const string _linkPath = "/host";

    public async ValueTask TerminalRejectionAsync(
        PublicChatTerminalRejection rejection,
        CancellationToken cancellationToken
    )
    {
        if (!string.Equals(rejection.ProviderCode, _followersOnlyCode, StringComparison.Ordinal))
        {
            return;
        }

        var channel = Login.Normalize(rejection.Channel);
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        var host = await ResolveHostAsync(channel, cancellationToken);
        if (host is null)
        {
            return;
        }

        await alerts
            .Create(
                host.Id,
                DurableAlertSeverity.Warning,
                _source,
                _sourceKey,
                "Follower-only chat blocked the bot",
                $"Twitch rejected a message from the active bot in #{host.Login} because follower-only chat applies. Open Channel setup to check the bot account.",
                _linkPath
            )
            .ExecuteAsync(cancellationToken);
    }

    private async Task<FollowerOnlyChatAlertHost?> ResolveHostAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .Where(host => host.Login == channel)
            .Select(host => new FollowerOnlyChatAlertHost(host.Id, host.Login))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private sealed record FollowerOnlyChatAlertHost(int Id, string Login);
}
