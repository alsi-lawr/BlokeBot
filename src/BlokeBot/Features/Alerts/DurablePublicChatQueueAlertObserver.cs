using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.Alerts;

internal sealed class DurablePublicChatQueueAlertObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    DurableAlertService alerts,
    ILogger<DurablePublicChatQueueAlertObserver> log
) : IPublicChatQueueAlertObserver
{
    private const string Source = "twitch-outbound-queue";
    private const string LinkPath = "/alerts";

    public async ValueTask QueueBackedUpAsync(
        PublicChatQueueBacklog backlog,
        CancellationToken cancellationToken
    )
    {
        var channel = TwitchLogin.Normalize(backlog.Channel);
        if (string.IsNullOrWhiteSpace(channel))
            return;

        var host = await ResolveHostAsync(channel, cancellationToken);
        if (host is null)
        {
            log.LogInformation(
                "Skipped public chat queue durable alert for unknown channel #{Channel}.",
                channel
            );
            return;
        }

        await alerts.CreateOrGetActiveAsync(
            host.Id,
            DurableAlertSeverity.Warning,
            Source,
            SourceKey(channel, backlog.OldestPendingAt),
            "Chat messages are delayed",
            Message(host.Login, backlog),
            LinkPath,
            cancellationToken
        );
    }

    private async Task<QueueAlertHost?> ResolveHostAsync(string channel, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == channel)
            .Select(x => new QueueAlertHost(x.Id, x.Login))
            .SingleOrDefaultAsync(ct);
    }

    private static string Message(string hostLogin, PublicChatQueueBacklog backlog) =>
        $"BlokeBot has {backlog.PendingCount} messages waiting to be sent in #{hostLogin}. The oldest has been waiting about {FormatAge(backlog.OldestPendingAge)}.";

    private static string SourceKey(string channel, DateTimeOffset oldestPendingAt) =>
        $"{channel}:{oldestPendingAt.UtcDateTime:O}";

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes >= 1)
            return $"{Math.Round(age.TotalMinutes, 1)} minutes";

        return $"{Math.Max(1, (int)Math.Round(age.TotalSeconds))} seconds";
    }

    private sealed record QueueAlertHost(int Id, string Login);
}
