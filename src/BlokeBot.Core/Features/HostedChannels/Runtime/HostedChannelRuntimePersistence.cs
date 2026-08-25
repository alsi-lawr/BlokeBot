using System.Linq.Expressions;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

internal static class HostedChannelRuntimePersistence
{
    internal static readonly Expression<Func<BotHost, bool>> CanStart = host =>
        host.BotRuntimeState != BotChannelRuntimeState.Starting
        && host.BotRuntimeState != BotChannelRuntimeState.Started;

    internal static readonly Expression<Func<BotHost, bool>> CanStop = host =>
        host.BotRuntimeState != BotChannelRuntimeState.Stopping
        && host.BotRuntimeState != BotChannelRuntimeState.Stopped;

    internal static PendingAccountRuntimeChange PendingChangeFor(
        HostedChannelAccountSelectionRuntimeChange runtimeChange
    ) =>
        runtimeChange switch
        {
            HostedChannelAccountSelectionRuntimeChange.None => PendingAccountRuntimeChange.None,
            HostedChannelAccountSelectionRuntimeChange.Restart =>
                PendingAccountRuntimeChange.Restart,
            HostedChannelAccountSelectionRuntimeChange.Stop => PendingAccountRuntimeChange.Stop,
        };

    internal static async Task<HostedChannelRuntimeTransitionOutcome> TransitionAsync(
        BlokeBotDbContext db,
        int hostId,
        Expression<Func<BotHost, bool>> canTransition,
        BotChannelRuntimeState target,
        CancellationToken cancellationToken
    )
    {
        var transitioned = await TryTransitionAsync(
            db,
            hostId,
            canTransition,
            target,
            cancellationToken
        );
        return transitioned ? HostedChannelRuntimeTransitionOutcome.Transitioned
            : await db.Hosts.AnyAsync(host => host.Id == hostId, cancellationToken)
                ? HostedChannelRuntimeTransitionOutcome.Unchanged
            : HostedChannelRuntimeTransitionOutcome.HostNotFound;
    }

    internal static async Task<bool> TryTransitionAsync(
        BlokeBotDbContext db,
        int hostId,
        Expression<Func<BotHost, bool>> canTransition,
        BotChannelRuntimeState target,
        CancellationToken cancellationToken
    ) =>
        await db
            .Hosts.Where(host => host.Id == hostId)
            .Where(canTransition)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(host => host.BotRuntimeState, target)
                        .SetProperty(host => host.BotRuntimeStateChangedAtUtc, DateTime.UtcNow),
                cancellationToken
            ) > 0;

    internal static Task<bool> TryTransitionAsync(
        BlokeBotDbContext db,
        int hostId,
        PendingAccountRuntimeChange runtimeChange,
        CancellationToken cancellationToken
    ) =>
        runtimeChange switch
        {
            PendingAccountRuntimeChange.None => Task.FromResult(false),
            PendingAccountRuntimeChange.Restart => TryTransitionAsync(
                db,
                hostId,
                host =>
                    host.BotRuntimeState == BotChannelRuntimeState.Starting
                    || host.BotRuntimeState == BotChannelRuntimeState.Started,
                BotChannelRuntimeState.Starting,
                cancellationToken
            ),
            PendingAccountRuntimeChange.Stop => TryTransitionAsync(
                db,
                hostId,
                host =>
                    host.BotRuntimeState == BotChannelRuntimeState.Starting
                    || host.BotRuntimeState == BotChannelRuntimeState.Started,
                BotChannelRuntimeState.Stopped,
                cancellationToken
            ),
            PendingAccountRuntimeChange.ForceStop => TryTransitionAsync(
                db,
                hostId,
                host => host.BotRuntimeState != BotChannelRuntimeState.Stopped,
                BotChannelRuntimeState.Stopped,
                cancellationToken
            ),
        };

    internal static async Task<HostedChannelRuntimeRecovery> RecoverInterruptedStopsAsync(
        BlokeBotDbContext db,
        CancellationToken cancellationToken
    )
    {
        var hostIds = await db
            .Hosts.AsNoTracking()
            .Where(host => host.BotRuntimeState == BotChannelRuntimeState.Stopping)
            .Select(host => host.Id)
            .ToArrayAsync(cancellationToken);
        var count = await db
            .Hosts.Where(host => host.BotRuntimeState == BotChannelRuntimeState.Stopping)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(host => host.BotRuntimeState, BotChannelRuntimeState.Stopped)
                        .SetProperty(host => host.BotRuntimeStateChangedAtUtc, DateTime.UtcNow),
                cancellationToken
            );
        return new HostedChannelRuntimeRecovery(hostIds, count);
    }

    internal static async Task<HostedChannelRuntimeCallbackHost?> LoadCallbackHostAsync(
        BlokeBotDbContext db,
        string normalizedChannelLogin,
        CancellationToken cancellationToken
    ) =>
        await db
            .Hosts.AsNoTracking()
            .Where(host => host.Login == normalizedChannelLogin)
            .Select(host => new HostedChannelRuntimeCallbackHost(host.Id, host.BotRuntimeState))
            .SingleOrDefaultAsync(cancellationToken);

    internal static async Task<bool> TryConfirmAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyCollection<BotChannelRuntimeState> expected,
        BotChannelRuntimeState target,
        CancellationToken cancellationToken
    ) =>
        await db
            .Hosts.Where(host => host.Id == hostId && expected.Contains(host.BotRuntimeState))
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(host => host.BotRuntimeState, target)
                        .SetProperty(host => host.BotRuntimeStateChangedAtUtc, DateTime.UtcNow),
                cancellationToken
            ) > 0;
}

internal sealed record HostedChannelRuntimeRecovery(int[] HostIds, int Count);

internal sealed record HostedChannelRuntimeCallbackHost(int Id, BotChannelRuntimeState State);
