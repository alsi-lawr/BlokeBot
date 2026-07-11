namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchBotRuntimeStatusStore : ITwitchBotRuntimeStatusAccessor
{
    private readonly object gate = new();
    private TwitchBotRuntimeStatus current = new(false, false, []);
    private long activeEventSubScopeId;

    public event Action? Changed;

    public TwitchBotRuntimeStatus Current
    {
        get
        {
            lock (gate)
                return current;
        }
    }

    public void SetAuthorized(bool isAuthorized)
    {
        Action? changed;
        lock (gate)
        {
            current = current with { IsAuthorized = isAuthorized };
            changed = Changed;
        }

        changed?.Invoke();
    }

    public void SetConnected(bool isConnected, IReadOnlyList<string> channels)
    {
        Action? changed;
        lock (gate)
        {
            current = current with
            {
                IsAuthorized = isConnected || current.IsAuthorized,
                IsConnected = isConnected,
                ConnectedChannels = isConnected ? channels : [],
            };
            changed = Changed;
        }

        changed?.Invoke();
    }

    internal void ActivateEventSubScope(long scopeId)
    {
        lock (gate)
            activeEventSubScopeId = scopeId;
    }

    internal void SetEventSubStatus(
        long scopeId,
        bool isAuthorized,
        IReadOnlyList<string> connectedChannels
    )
    {
        Action? changed;
        lock (gate)
        {
            if (activeEventSubScopeId != scopeId)
                return;

            current = current with
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
        lock (gate)
        {
            if (activeEventSubScopeId != scopeId)
                return;

            activeEventSubScopeId = 0;
            current = current with { IsConnected = false, ConnectedChannels = [] };
            changed = Changed;
        }

        changed?.Invoke();
    }
}
