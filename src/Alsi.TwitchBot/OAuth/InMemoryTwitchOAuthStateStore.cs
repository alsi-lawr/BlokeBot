namespace Alsi.TwitchBot;

internal sealed class InMemoryTwitchOAuthStateStore : ITwitchOAuthStateStore
{
    private readonly HashSet<string> states = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public string Issue()
    {
        var state = Guid.NewGuid().ToString("N");
        lock (gate)
            states.Add(state);
        return state;
    }

    public bool Consume(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return false;

        lock (gate)
            return states.Remove(state);
    }
}
