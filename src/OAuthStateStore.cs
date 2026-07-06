public interface IOAuthStateStore
{
    string Issue();
    bool Consume(string state);
}

public sealed class InMemoryOAuthStateStore : IOAuthStateStore
{
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public string Issue()
    {
        var s = Guid.NewGuid().ToString("N");
        lock (_gate)
            _states.Add(s);
        return s;
    }

    public bool Consume(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return false;
        lock (_gate)
            return _states.Remove(state);
    }
}
