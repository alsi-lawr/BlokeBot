namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchBotRuntimeStatusStore : ITwitchBotRuntimeStatusAccessor
{
    private readonly object _gate = new();
    private TwitchBotRuntimeStatus _current = new(false, false, []);
    private long _activeEventSubScopeId;

    public event Action? Changed;

    public TwitchBotRuntimeStatus Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void SetAuthorized(bool isAuthorized)
    {
        Action? changed;
        lock (_gate)
        {
            _current = _current with { IsAuthorized = isAuthorized };
            changed = Changed;
        }

        changed?.Invoke();
    }

    public void SetConnected(bool isConnected, IReadOnlyList<string> channels)
    {
        Action? changed;
        lock (_gate)
        {
            _current = _current with
            {
                IsAuthorized = isConnected || _current.IsAuthorized,
                IsConnected = isConnected,
                ConnectedChannels = isConnected ? channels : [],
            };
            changed = Changed;
        }

        changed?.Invoke();
    }

    internal void ActivateEventSubScope(long scopeId)
    {
        lock (_gate)
        {
            _activeEventSubScopeId = scopeId;
        }
    }

    internal void SetEventSubStatus(
        long scopeId,
        bool isAuthorized,
        IReadOnlyList<string> connectedChannels
    )
    {
        Action? changed;
        lock (_gate)
        {
            if (_activeEventSubScopeId != scopeId)
            {
                return;
            }

            _current = _current with
            {
                IsAuthorized = isAuthorized || connectedChannels.Count > 0,
                IsConnected = connectedChannels.Count > 0,
                ConnectedChannels = connectedChannels,
            };
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
            _current = _current with { IsConnected = false, ConnectedChannels = [] };
            changed = Changed;
        }

        changed?.Invoke();
    }
}
