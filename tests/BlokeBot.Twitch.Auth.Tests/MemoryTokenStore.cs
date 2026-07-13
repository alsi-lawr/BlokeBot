using BlokeBot.Twitch.Auth;

namespace BlokeBot.Twitch.Auth.Tests;

internal sealed class MemoryTokenStore : ITokenStore
{
    public TokenSet? Loaded { get; set; }

    public Exception? LoadException { get; set; }

    public Exception? SaveException { get; set; }

    public int LoadCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public TokenSet? Saved { get; private set; }

    public async Task<TokenSet?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        LoadCalls++;
        await Task.Yield();
        if (LoadException is not null)
        {
            throw LoadException;
        }

        return Loaded;
    }

    public async Task SaveAsync(string path, TokenSet tokenSet, CancellationToken cancellationToken)
    {
        SaveCalls++;
        await Task.Yield();
        if (SaveException is not null)
        {
            throw SaveException;
        }

        Saved = tokenSet;
    }
}
