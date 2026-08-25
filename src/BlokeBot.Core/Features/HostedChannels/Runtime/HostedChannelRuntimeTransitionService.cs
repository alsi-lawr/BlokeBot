using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeTransitionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes
) : IDisposable
{
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly Dictionary<int, BotChannelSessionIdentity> _activeSessions = [];

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
            replaceSession: true,
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
            replaceSession: false,
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
            replaceSession: canStart,
            clearSession: !canStart,
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
            replaceSession: false,
            clearSession: true,
            cancellationToken
        );

    internal async Task<BotChannelTarget> GetOrCreateSessionTargetAsync(
        int hostId,
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            if (!_activeSessions.TryGetValue(hostId, out var identity))
            {
                identity = BotChannelSessionIdentity.Create();
                _activeSessions[hostId] = identity;
            }

            return new BotChannelTarget(channelLogin, identity);
        }
        finally
        {
            _ = _transitionGate.Release();
        }
    }

    internal Task<bool> ConfirmStartedAsync(
        string normalizedChannelLogin,
        BotChannelSessionIdentity sessionIdentity,
        CancellationToken cancellationToken
    ) =>
        ConfirmAsync(
            normalizedChannelLogin,
            sessionIdentity,
            BotChannelRuntimeState.Starting,
            BotChannelRuntimeState.Started,
            clearSession: false,
            cancellationToken
        );

    internal Task<bool> ConfirmStoppedAsync(
        string normalizedChannelLogin,
        BotChannelSessionIdentity sessionIdentity,
        CancellationToken cancellationToken
    ) =>
        ConfirmAsync(
            normalizedChannelLogin,
            sessionIdentity,
            [BotChannelRuntimeState.Stopping, BotChannelRuntimeState.Started],
            BotChannelRuntimeState.Stopped,
            clearSession: true,
            cancellationToken
        );

    internal async Task<int> RecoverInterruptedStopsAsync(CancellationToken cancellationToken)
    {
        int recovered;
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var interruptedHostIds = await db
                .Hosts.AsNoTracking()
                .Where(host => host.BotRuntimeState == BotChannelRuntimeState.Stopping)
                .Select(host => host.Id)
                .ToArrayAsync(cancellationToken);
            recovered = await db
                .Hosts.Where(host => host.BotRuntimeState == BotChannelRuntimeState.Stopping)
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
            foreach (var hostId in interruptedHostIds)
            {
                _ = _activeSessions.Remove(hostId);
            }
        }
        finally
        {
            _ = _transitionGate.Release();
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
        bool replaceSession,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var outcome = await BeginOperationAsync(
            db,
            hostId,
            canTransition,
            target,
            replaceSession,
            clearSession: false,
            cancellationToken
        );
        if (outcome is HostedChannelRuntimeTransitionOutcome.Transitioned)
        {
            _ = await changes.NotifyChangedAsync(cancellationToken);
        }

        return outcome;
    }

    private async Task<HostedChannelRuntimeTransitionOutcome> BeginOperationAsync(
        BlokeBotDbContext db,
        int hostId,
        System.Linq.Expressions.Expression<Func<BotHost, bool>> canTransition,
        BotChannelRuntimeState target,
        bool replaceSession,
        bool clearSession,
        CancellationToken cancellationToken
    )
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            var changed = await db
                .Hosts.Where(host => host.Id == hostId)
                .Where(canTransition)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(host => host.BotRuntimeState, target)
                            .SetProperty(host => host.BotRuntimeStateChangedAtUtc, DateTime.UtcNow),
                    cancellationToken
                );
            if (changed > 0)
            {
                if (replaceSession)
                {
                    _activeSessions[hostId] = BotChannelSessionIdentity.Create();
                }
                else if (clearSession)
                {
                    _ = _activeSessions.Remove(hostId);
                }

                return HostedChannelRuntimeTransitionOutcome.Transitioned;
            }

            return await db.Hosts.AnyAsync(host => host.Id == hostId, cancellationToken)
                ? HostedChannelRuntimeTransitionOutcome.Unchanged
                : HostedChannelRuntimeTransitionOutcome.HostNotFound;
        }
        finally
        {
            _ = _transitionGate.Release();
        }
    }

    private async Task<bool> ConfirmAsync(
        string normalizedChannelLogin,
        BotChannelSessionIdentity sessionIdentity,
        BotChannelRuntimeState expected,
        BotChannelRuntimeState target,
        bool clearSession,
        CancellationToken cancellationToken
    ) =>
        await ConfirmAsync(
            normalizedChannelLogin,
            sessionIdentity,
            [expected],
            target,
            clearSession,
            cancellationToken
        );

    private async Task<bool> ConfirmAsync(
        string normalizedChannelLogin,
        BotChannelSessionIdentity sessionIdentity,
        IReadOnlyCollection<BotChannelRuntimeState> expected,
        BotChannelRuntimeState target,
        bool clearSession,
        CancellationToken cancellationToken
    )
    {
        var changed = false;
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var host = await db
                .Hosts.AsNoTracking()
                .Where(host => host.Login == normalizedChannelLogin)
                .Select(host => new RuntimeCallbackHost(host.Id, host.BotRuntimeState))
                .SingleOrDefaultAsync(cancellationToken);
            if (
                host is null
                || !_activeSessions.TryGetValue(host.Id, out var activeSession)
                || !ReferenceEquals(activeSession, sessionIdentity)
                || (host.State != target && !expected.Contains(host.State))
            )
            {
                return false;
            }

            if (host.State != target)
            {
                changed =
                    await db
                        .Hosts.Where(candidate =>
                            candidate.Id == host.Id && expected.Contains(candidate.BotRuntimeState)
                        )
                        .ExecuteUpdateAsync(
                            setters =>
                                setters
                                    .SetProperty(candidate => candidate.BotRuntimeState, target)
                                    .SetProperty(
                                        candidate => candidate.BotRuntimeStateChangedAtUtc,
                                        DateTime.UtcNow
                                    ),
                            cancellationToken
                        ) > 0;
            }

            if (clearSession)
            {
                _ = _activeSessions.Remove(host.Id);
            }
        }
        finally
        {
            _ = _transitionGate.Release();
        }

        if (changed)
        {
            _ = await changes.NotifyChangedAsync(cancellationToken);
        }

        return true;
    }

    private sealed record RuntimeCallbackHost(int Id, BotChannelRuntimeState State);

    public void Dispose() => _transitionGate.Dispose();
}

internal enum HostedChannelRuntimeTransitionOutcome
{
    HostNotFound,
    Unchanged,
    Transitioned,
}
