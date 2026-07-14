using System.Threading.Channels;
using BlokeBot.Functional;
using BlokeBot.Twitch.Auth;

namespace BlokeBot.Twitch.Auth.Tests;

internal sealed class MemoryTokenStore : ITokenStore
{
    public TokenSet? Loaded { get; set; }

    public Exception? LoadException { get; set; }

    public Exception? SaveException { get; set; }

    public Channel<bool>? LoadStarted { get; init; }

    public Channel<bool>? ContinueLoad { get; init; }

    public int LoadCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public TokenSet? Saved { get; private set; }

    public async Task<Option<TokenSet>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        LoadCalls++;
        await Task.Yield();
        if (LoadStarted is not null)
        {
            await LoadStarted.Writer.WriteAsync(true, cancellationToken);
        }

        if (ContinueLoad is not null)
        {
            await ContinueLoad.Reader.ReadAsync(cancellationToken);
        }

        if (LoadException is not null)
        {
            throw LoadException;
        }

        return Option<TokenSet>.FromNullable(Loaded);
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
