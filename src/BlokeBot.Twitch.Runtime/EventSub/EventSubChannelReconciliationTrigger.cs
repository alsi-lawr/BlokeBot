namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubChannelReconciliationTrigger(IBotChannelProvider channels)
    : IEventSubChannelReconciliationTrigger
{
    private readonly object _gate = new();
    private EventSubChannelSession? _session;

    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        EventSubChannelSession? session;
        lock (_gate)
        {
            session = _session;
        }

        if (session is null)
        {
            return;
        }

        await session.TriggerReconciliationAndDrainAsync(
            channels.GetChannelsAsync,
            EventSubChannelRecoveryTrigger.Explicit,
            cancellationToken
        );
    }

    public async Task ReconcileRevocationAsync(
        string subscriptionId,
        CancellationToken cancellationToken
    )
    {
        EventSubChannelSession? session;
        lock (_gate)
        {
            session = _session;
        }

        if (session is null)
        {
            return;
        }

        await session.RepairRevokedSubscriptionAndDrainAsync(
            subscriptionId,
            channels.GetChannelsAsync,
            cancellationToken
        );
    }

    internal IDisposable Register(EventSubChannelSession session)
    {
        lock (_gate)
        {
            _session = session;
        }

        return new Registration(this, session);
    }

    private void Unregister(EventSubChannelSession session)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_session, session))
            {
                _session = null;
            }
        }
    }

    private sealed class Registration(
        EventSubChannelReconciliationTrigger owner,
        EventSubChannelSession session
    ) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.Unregister(session);
        }
    }
}
