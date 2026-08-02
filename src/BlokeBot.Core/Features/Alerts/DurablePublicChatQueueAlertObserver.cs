using System.Diagnostics;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Core.Features.Alerts;

internal sealed class DurablePublicChatQueueAlertObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    DurableAlertService alerts,
    ILogger<DurablePublicChatQueueAlertObserver> log
) : IPublicChatQueueAlertObserver
{
    private const string _source = "twitch-outbound-queue";
    private const string _linkPath = "/alerts";

    public async ValueTask QueueBackedUpAsync(
        PublicChatQueueBacklog backlog,
        CancellationToken cancellationToken
    )
    {
        var channel = Login.Normalize(backlog.Channel);
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        var hostResult = await ResolveHost(channel).ExecuteAsync(cancellationToken);
        var host = hostResult.Match(
            option => option.Match<QueueAlertHost?>(value => value, () => null),
            _ => throw new UnreachableException()
        );
        if (host is null)
        {
            log.LogInformation(
                "Skipped public chat queue durable alert for unknown channel #{Channel}.",
                channel
            );
            return;
        }

        await alerts
            .Create(
                host.Id,
                DurableAlertSeverity.Warning,
                _source,
                SourceKey(channel, backlog.OldestPendingAt),
                "Chat messages are delayed",
                Message(host.Login, backlog),
                _linkPath
            )
            .ExecuteAsync(cancellationToken);
    }

    private IO<Option<QueueAlertHost>, Never> ResolveHost(string channel) =>
        IO<Option<QueueAlertHost>, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var host = await db
                .Hosts.AsNoTracking()
                .Where(x => x.Login == channel)
                .Select(x => new QueueAlertHost(x.Id, x.Login))
                .SingleOrDefaultAsync(ct);
            return Result<Option<QueueAlertHost>, Never>.Success(
                Option<QueueAlertHost>.FromNullable(host)
            );
        });

    private static string Message(string hostLogin, PublicChatQueueBacklog backlog) =>
        $"BlokeBot has {backlog.PendingCount} messages waiting to be sent in #{hostLogin}. The oldest has been waiting about {FormatAge(backlog.OldestPendingAge)}.";

    private static string SourceKey(string channel, DateTimeOffset oldestPendingAt) =>
        $"{channel}:{oldestPendingAt.UtcDateTime:O}";

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes >= 1)
        {
            return $"{Math.Round(age.TotalMinutes, 1)} minutes";
        }

        return $"{Math.Max(1, (int)Math.Round(age.TotalSeconds))} seconds";
    }

    private sealed record QueueAlertHost(int Id, string Login);
}
