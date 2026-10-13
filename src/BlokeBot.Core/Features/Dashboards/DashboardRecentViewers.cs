using System.Collections.Immutable;
using System.Data.Common;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Identity;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal readonly record struct DashboardRecentViewersChanged(DashboardHostId Host);

internal sealed class DashboardRecentViewers(
    IDbContextFactory<BlokeBotDbContext> database,
    DashboardAuthority authority,
    TimeProvider clock,
    EventBus<DashboardRecentViewersChanged> events,
    ILogger<DashboardRecentViewers> log
) : IChatMessageObserver
{
    internal const int Limit = 30;

    public async ValueTask MessageReceivedAsync(ChatMessage message, CancellationToken ct)
    {
        var observedAt = clock.GetUtcNow().UtcDateTime;
        var channel = LoginName.Parse(message.Channel).Value;
        var login = LoginName.Parse(message.Login).Value;
        if (
            channel.Length == 0
            || login.Length == 0
            || !message.Tags.TryGetValue("user-id", out var userId)
            || string.IsNullOrWhiteSpace(userId)
        )
        {
            return;
        }
        userId = userId.Trim();
        var display =
            message.Tags.TryGetValue("display-name", out var name)
            && !string.IsNullOrWhiteSpace(name)
                ? name
                : login;
        await using var db = await database.CreateDbContextAsync(ct);
        var hostId = await db
            .Hosts.Where(h => h.Login == channel)
            .Select(h => (int?)h.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is null)
        {
            return;
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await MainDatabaseStatements.LockHostAsync(db, hostId.Value, ct) == 0)
        {
            return;
        }
        var rows = await db.RecentChannelChatters.Where(x => x.HostId == hostId).ToListAsync(ct);
        var chatter = rows.SingleOrDefault(x => x.TwitchUserId == userId);
        if (chatter is not null && chatter.ObservedAtUtc >= observedAt)
        {
            return;
        }
        if (chatter is null)
        {
            chatter = new() { HostId = hostId.Value, TwitchUserId = userId };
            rows.Add(chatter);
            _ = db.RecentChannelChatters.Add(chatter);
        }
        chatter.Login = login;
        chatter.DisplayName = display;
        chatter.ObservedAtUtc = observedAt;
        var evicted = rows.OrderByDescending(x => x.ObservedAtUtc)
            .ThenBy(x => x.TwitchUserId, StringComparer.Ordinal)
            .Skip(Limit)
            .ToArray();
        db.RecentChannelChatters.RemoveRange(evicted);
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (!evicted.Contains(chatter))
        {
            _ = await events.PublishAsync(new(new(hostId.Value)), CancellationToken.None);
        }
    }

    internal async Task<DashboardResult<ImmutableArray<CustomCommandViewer>>> ReadAsync(
        AuthenticatedSession session,
        DashboardHostId host,
        CancellationToken ct
    )
    {
        try
        {
            if (
                await authority.AuthorizeAsync(session, host, ct)
                is DashboardResult<DashboardHostId>.Rejected rejected
            )
            {
                return new DashboardResult<ImmutableArray<CustomCommandViewer>>.Rejected(
                    rejected.Reason
                );
            }
            await using var db = await database.CreateDbContextAsync(ct);
            var viewers = await db
                .RecentChannelChatters.AsNoTracking()
                .Where(x => x.HostId == host.Value)
                .OrderByDescending(x => x.ObservedAtUtc)
                .ThenBy(x => x.TwitchUserId)
                .Take(Limit)
                .Select(x => new CustomCommandViewer(x.TwitchUserId, x.Login, x.DisplayName))
                .ToArrayAsync(ct);
            return new DashboardResult<ImmutableArray<CustomCommandViewer>>.Succeeded([.. viewers]);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            log.LogWarning(
                exception,
                "Recent dashboard viewers unavailable for host {HostId}.",
                host.Value
            );
            return new DashboardResult<ImmutableArray<CustomCommandViewer>>.Rejected(
                DashboardRejection.AuthorityUnavailable
            );
        }
    }

    internal IDisposable Subscribe(
        DashboardHostId host,
        ObserverIdentity observer,
        Func<EventNotification<DashboardRecentViewersChanged>, CancellationToken, ValueTask> handler
    ) => events.Subscribe(new DashboardRecentViewersChanged(host), observer, handler);
}
