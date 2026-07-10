using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.Alerts;

internal sealed class DurableOutboundQueueAlertObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    DurableAlertService alerts,
    OutboundQueueAlertSubscriberDispatcher subscribers,
    ILogger<DurableOutboundQueueAlertObserver> log
) : ITwitchOutboundQueueAlertObserver
{
    private const string Source = "twitch-outbound-queue";
    private const string LinkPath = "/alerts";

    public async ValueTask QueueBackedUpAsync(
        TwitchOutboundQueueBacklog backlog,
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
                "Skipped outbound queue durable alert for unknown channel #{Channel}.",
                channel
            );
            return;
        }

        var result = await alerts.CreateOrGetActiveAsync(
            host.Id,
            DurableAlertSeverity.Warning,
            Source,
            SourceKey(channel, backlog.OldestPendingAt),
            "Outbound chat queue delayed",
            Message(host.Login, backlog),
            LinkPath,
            cancellationToken
        );
        if (!result.Created)
            return;

        await subscribers.AlertCreatedAsync(
            new OutboundQueueAlertNotification(
                result.Alert.Id,
                host.Id,
                host.Login,
                host.TwitchUserId,
                backlog.PendingCount,
                backlog.OldestPendingAge
            ),
            cancellationToken
        );
    }

    private async Task<QueueAlertHost?> ResolveHostAsync(string channel, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == channel)
            .Select(x => new QueueAlertHost(x.Id, x.Login, x.TwitchUserId))
            .SingleOrDefaultAsync(ct);
    }

    private static string Message(string hostLogin, TwitchOutboundQueueBacklog backlog) =>
        $"BlokeBot has {backlog.PendingCount} pending outbound chat messages for #{hostLogin}. The oldest has waited about {FormatAge(backlog.OldestPendingAge)}.";

    private static string SourceKey(string channel, DateTimeOffset oldestPendingAt) =>
        $"{channel}:{oldestPendingAt.UtcDateTime:O}";

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes >= 1)
            return $"{Math.Round(age.TotalMinutes, 1)} minutes";

        return $"{Math.Max(1, (int)Math.Round(age.TotalSeconds))} seconds";
    }

    private sealed record QueueAlertHost(int Id, string Login, string? TwitchUserId);
}
