using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeTransitionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes
)
{
    internal Task<HostedChannelRuntimeTransitionOutcome> RequestStartAsync(
        int hostId,
        CancellationToken cancellationToken
    ) =>
        BeginOperationAndNotifyAsync(
            hostId,
            host =>
                host.BotRuntimeState != BotChannelRuntimeState.Starting
                && host.BotRuntimeState != BotChannelRuntimeState.Started,
            BotChannelRuntimeState.Starting,
            cancellationToken
        );

    internal Task<HostedChannelRuntimeTransitionOutcome> RequestStopAsync(
        int hostId,
        CancellationToken cancellationToken
    ) =>
        BeginOperationAndNotifyAsync(
            hostId,
            host =>
                host.BotRuntimeState != BotChannelRuntimeState.Stopping
                && host.BotRuntimeState != BotChannelRuntimeState.Stopped,
            BotChannelRuntimeState.Stopping,
            cancellationToken
        );

    internal Task<HostedChannelRuntimeTransitionOutcome> RestartAfterAccountChangeAsync(
        BlokeBotDbContext db,
        int hostId,
        bool canStart,
        CancellationToken cancellationToken
    ) =>
        BeginOperationAsync(
            db,
            hostId,
            host =>
                host.BotRuntimeState == BotChannelRuntimeState.Starting
                || host.BotRuntimeState == BotChannelRuntimeState.Started,
            canStart ? BotChannelRuntimeState.Starting : BotChannelRuntimeState.Stopped,
            cancellationToken
        );

    internal Task<HostedChannelRuntimeTransitionOutcome> ForceStoppedForCredentialPolicyAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        BeginOperationAsync(
            db,
            hostId,
            host => host.BotRuntimeState != BotChannelRuntimeState.Stopped,
            BotChannelRuntimeState.Stopped,
            cancellationToken
        );

    internal Task<bool> ConfirmStartedAsync(
        string normalizedChannelLogin,
        CancellationToken cancellationToken
    ) =>
        ConfirmAsync(
            normalizedChannelLogin,
            host => host.BotRuntimeState == BotChannelRuntimeState.Starting,
            BotChannelRuntimeState.Started,
            cancellationToken
        );

    internal Task<bool> ConfirmStoppedAsync(
        string normalizedChannelLogin,
        CancellationToken cancellationToken
    ) =>
        ConfirmAsync(
            normalizedChannelLogin,
            host =>
                host.BotRuntimeState == BotChannelRuntimeState.Stopping
                || host.BotRuntimeState == BotChannelRuntimeState.Started,
            BotChannelRuntimeState.Stopped,
            cancellationToken
        );

    internal async Task<int> RecoverInterruptedStopsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var interrupted = await db
            .Hosts.AsNoTracking()
            .Where(host => host.BotRuntimeState == BotChannelRuntimeState.Stopping)
            .Select(host => new HostedChannelRuntimeOperation(host.Id, host.BotRuntimeGeneration))
            .ToArrayAsync(cancellationToken);

        var recovered = 0;
        foreach (var operation in interrupted)
        {
            recovered += await db
                .Hosts.Where(host =>
                    host.Id == operation.HostId
                    && host.BotRuntimeGeneration == operation.Generation
                    && host.BotRuntimeState == BotChannelRuntimeState.Stopping
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                host => host.BotRuntimeState,
                                BotChannelRuntimeState.Stopped
                            )
                            .SetProperty(host => host.BotRuntimeStateChangedAtUtc, DateTime.UtcNow),
                    cancellationToken
                );
        }

        if (recovered > 0)
        {
            _ = await changes.NotifyChangedAsync(cancellationToken);
        }

        return recovered;
    }

    private async Task<HostedChannelRuntimeTransitionOutcome> BeginOperationAndNotifyAsync(
        int hostId,
        System.Linq.Expressions.Expression<Func<BotHost, bool>> canTransition,
        BotChannelRuntimeState target,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var outcome = await BeginOperationAsync(
            db,
            hostId,
            canTransition,
            target,
            cancellationToken
        );
        if (outcome is HostedChannelRuntimeTransitionOutcome.Transitioned)
        {
            _ = await changes.NotifyChangedAsync(cancellationToken);
        }

        return outcome;
    }

    private static async Task<HostedChannelRuntimeTransitionOutcome> BeginOperationAsync(
        BlokeBotDbContext db,
        int hostId,
        System.Linq.Expressions.Expression<Func<BotHost, bool>> canTransition,
        BotChannelRuntimeState target,
        CancellationToken cancellationToken
    )
    {
        var changed = await db
            .Hosts.Where(host => host.Id == hostId)
            .Where(canTransition)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(host => host.BotRuntimeState, target)
                        .SetProperty(host => host.BotRuntimeStateChangedAtUtc, DateTime.UtcNow)
                        .SetProperty(
                            host => host.BotRuntimeGeneration,
                            host => host.BotRuntimeGeneration + 1
                        ),
                cancellationToken
            );
        return changed > 0 ? HostedChannelRuntimeTransitionOutcome.Transitioned
            : await db.Hosts.AnyAsync(host => host.Id == hostId, cancellationToken)
                ? HostedChannelRuntimeTransitionOutcome.Unchanged
            : HostedChannelRuntimeTransitionOutcome.HostNotFound;
    }

    private async Task<bool> ConfirmAsync(
        string normalizedChannelLogin,
        System.Linq.Expressions.Expression<Func<BotHost, bool>> canTransition,
        BotChannelRuntimeState target,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var operation = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Login == normalizedChannelLogin)
            .Select(host => new HostedChannelRuntimeOperation(host.Id, host.BotRuntimeGeneration))
            .SingleOrDefaultAsync(cancellationToken);
        if (operation is null)
        {
            return false;
        }

        var changed = await db
            .Hosts.Where(host =>
                host.Id == operation.HostId && host.BotRuntimeGeneration == operation.Generation
            )
            .Where(canTransition)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(host => host.BotRuntimeState, target)
                        .SetProperty(host => host.BotRuntimeStateChangedAtUtc, DateTime.UtcNow),
                cancellationToken
            );
        if (changed == 0)
        {
            return false;
        }

        _ = await changes.NotifyChangedAsync(cancellationToken);
        return true;
    }

    private sealed record HostedChannelRuntimeOperation(int HostId, long Generation);
}

internal enum HostedChannelRuntimeTransitionOutcome
{
    HostNotFound,
    Unchanged,
    Transitioned,
}
