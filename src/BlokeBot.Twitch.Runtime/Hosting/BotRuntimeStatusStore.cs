namespace BlokeBot.Twitch.Runtime;

internal sealed class BotRuntimeStatusStore : IBotRuntimeStatusAccessor
{
    private readonly object _gate = new();
    private BotRuntimeStatus _current = new BotRuntimeStatus.Unauthorized();
    private long _activeEventSubScopeId;

    public event Action? Changed;

    public BotRuntimeStatus Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void MarkAuthorized() =>
        UpdateCurrent(static current =>
            current.Match<BotRuntimeStatus>(
                static _ => new BotRuntimeStatus.Authorized(),
                static authorized => authorized,
                static connected => connected
            )
        );

    public void MarkUnauthorized() => SetCurrent(new BotRuntimeStatus.Unauthorized());

    public void MarkConnected(IEnumerable<string> channels) =>
        SetCurrent(new BotRuntimeStatus.Connected(channels));

    public void MarkDisconnected() => UpdateCurrent(DisconnectedStatus);

    internal void ActivateEventSubScope(long scopeId)
    {
        lock (_gate)
        {
            _activeEventSubScopeId = scopeId;
        }
    }

    internal void SetEventSubStatus(long scopeId, BotRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        Action? changed;
        lock (_gate)
        {
            if (_activeEventSubScopeId != scopeId)
            {
                return;
            }

            _current = status;
            changed = Changed;
        }

        changed?.Invoke();
    }

    internal void DeactivateEventSubScope(long scopeId)
    {
        Action? changed;
        lock (_gate)
        {
            if (_activeEventSubScopeId != scopeId)
            {
                return;
            }

            _activeEventSubScopeId = 0;
            _current = DisconnectedStatus(_current);
            changed = Changed;
        }

        changed?.Invoke();
    }

    private void SetCurrent(BotRuntimeStatus status) => UpdateCurrent(_ => status);

    private void UpdateCurrent(Func<BotRuntimeStatus, BotRuntimeStatus> transition)
    {
        Action? changed;
        lock (_gate)
        {
            _current = transition(_current);
            changed = Changed;
        }

        changed?.Invoke();
    }

    private static BotRuntimeStatus DisconnectedStatus(BotRuntimeStatus status) =>
        status.Match<BotRuntimeStatus>(
            static unauthorized => unauthorized,
            static authorized => authorized,
            static _ => new BotRuntimeStatus.Authorized()
        );
}
