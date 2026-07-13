namespace BlokeBot.Twitch.Auth;

internal sealed class InMemoryOAuthStateStore : IOAuthStateStore
{
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public string Issue()
    {
        var state = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            _states.Add(state);
        }

        return state;
    }

    public bool Consume(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        lock (_gate)
        {
            return _states.Remove(state);
        }
    }
}
