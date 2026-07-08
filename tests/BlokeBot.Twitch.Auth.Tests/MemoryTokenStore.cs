using BlokeBot.Twitch.Auth;

namespace BlokeBot.Twitch.Auth.Tests;

internal sealed class MemoryTokenStore : ITwitchTokenStore
{
    public TwitchTokenSet? Loaded { get; set; }

    public int LoadCalls { get; private set; }
    public TwitchTokenSet? Saved { get; private set; }

    public Task<TwitchTokenSet?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        LoadCalls++;
        return Task.FromResult(Loaded);
    }

    public Task SaveAsync(string path, TwitchTokenSet tokenSet, CancellationToken cancellationToken)
    {
        Saved = tokenSet;
        return Task.CompletedTask;
    }
}
