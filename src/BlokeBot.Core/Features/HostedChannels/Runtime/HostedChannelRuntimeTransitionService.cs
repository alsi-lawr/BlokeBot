using System.Linq.Expressions;
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
    private readonly HostedChannelRuntimeSessions _activeSessions = new();

    internal Task<HostedChannelRuntimeTransitionOutcome> RequestStartAsync(
        int hostId,
        CancellationToken cancellationToken
    ) =>
        BeginOperationAndNotifyAsync(
            hostId,
            HostedChannelRuntimePersistence.CanStart,
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
            HostedChannelRuntimePersistence.CanStop,
            BotChannelRuntimeState.Stopping,
            replaceSession: false,
            cancellationToken
        );

    internal Task CommitAccountSelectionAsync(
        BlokeBotDbContext db,
        int hostId,
        HostedChannelAccountSelectionRuntimeChange runtimeChange,
        CancellationToken cancellationToken
    ) =>
        CommitPendingAccountChangesAsync(
            db,
            hostId,
            HostedChannelRuntimePersistence.PendingChangeFor(runtimeChange),
            cancellationToken
        );

    internal Task CommitCredentialPolicyStopAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        CommitPendingAccountChangesAsync(
            db,
            hostId,
            PendingAccountRuntimeChange.ForceStop,
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
            return new BotChannelTarget(channelLogin, _activeSessions.GetOrCreate(hostId));
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
            [BotChannelRuntimeState.Starting],
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
            var recovery = await HostedChannelRuntimePersistence.RecoverInterruptedStopsAsync(
                db,
                cancellationToken
            );
            recovered = recovery.Count;
            _activeSessions.Clear(recovery.HostIds);
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
        Expression<Func<BotHost, bool>> canTransition,
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

    private async Task CommitPendingAccountChangesAsync(
        BlokeBotDbContext db,
        int hostId,
        PendingAccountRuntimeChange runtimeChange,
        CancellationToken cancellationToken
    )
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            var runtimeChanged = await HostedChannelRuntimePersistence.TryTransitionAsync(
                db,
                hostId,
                runtimeChange,
                cancellationToken
            );
            await transaction.CommitAsync(cancellationToken);

            if (runtimeChanged)
            {
                if (runtimeChange is PendingAccountRuntimeChange.Restart)
                {
                    _activeSessions.Replace(hostId);
                }
                else
                {
                    _activeSessions.Clear(hostId);
                }
            }
        }
        finally
        {
            _ = _transitionGate.Release();
        }
    }

    private async Task<HostedChannelRuntimeTransitionOutcome> BeginOperationAsync(
        BlokeBotDbContext db,
        int hostId,
        Expression<Func<BotHost, bool>> canTransition,
        BotChannelRuntimeState target,
        bool replaceSession,
        bool clearSession,
        CancellationToken cancellationToken
    )
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            var outcome = await HostedChannelRuntimePersistence.TransitionAsync(
                db,
                hostId,
                canTransition,
                target,
                cancellationToken
            );
            if (outcome is HostedChannelRuntimeTransitionOutcome.Transitioned)
            {
                if (replaceSession)
                {
                    _activeSessions.Replace(hostId);
                }
                else if (clearSession)
                {
                    _activeSessions.Clear(hostId);
                }
            }

            return outcome;
        }
        finally
        {
            _ = _transitionGate.Release();
        }
    }

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
            var host = await HostedChannelRuntimePersistence.LoadCallbackHostAsync(
                db,
                normalizedChannelLogin,
                cancellationToken
            );
            if (
                host is null
                || !_activeSessions.IsCurrent(host.Id, sessionIdentity)
                || (host.State != target && !expected.Contains(host.State))
            )
            {
                return false;
            }

            if (host.State != target)
            {
                changed = await HostedChannelRuntimePersistence.TryConfirmAsync(
                    db,
                    host.Id,
                    expected,
                    target,
                    cancellationToken
                );
            }

            if (clearSession)
            {
                _activeSessions.Clear(host.Id);
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

    public void Dispose() => _transitionGate.Dispose();
}
