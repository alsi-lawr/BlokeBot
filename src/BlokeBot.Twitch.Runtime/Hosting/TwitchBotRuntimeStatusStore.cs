namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchBotRuntimeStatusStore : ITwitchBotRuntimeStatusAccessor
{
    private TwitchBotRuntimeStatus current = new(false, false, []);

    public event Action? Changed;

    public TwitchBotRuntimeStatus Current => current;

    public void SetAuthorized(bool isAuthorized)
    {
        var next = current with { IsAuthorized = isAuthorized };
        Set(next);
    }

    public void SetConnected(bool isConnected, IReadOnlyList<string> channels)
    {
        var next = current with
        {
            IsAuthorized = isConnected || current.IsAuthorized,
            IsConnected = isConnected,
            ConnectedChannels = isConnected ? channels : [],
        };
        Set(next);
    }

    private void Set(TwitchBotRuntimeStatus status)
    {
        current = status;
        Changed?.Invoke();
    }
}
