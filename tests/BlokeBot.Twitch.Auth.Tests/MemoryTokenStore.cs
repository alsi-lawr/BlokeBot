using System.Threading.Channels;
using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth.Tests;

internal sealed class MemoryTokenStore : ITokenStore
{
    public TokenSet? Loaded { get; set; }

    public Exception? LoadException { get; set; }

    public Exception? SaveException { get; set; }

    public Exception? DeleteException { get; set; }

    public Channel<bool>? LoadStarted { get; init; }

    public Channel<bool>? ContinueLoad { get; init; }

    public Channel<bool>? SaveStarted { get; init; }

    public Channel<bool>? ContinueSave { get; init; }

    public Channel<bool>? DeleteStarted { get; init; }

    public Channel<bool>? ContinueDelete { get; init; }

    public int LoadCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public int DeleteCalls { get; private set; }

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
            _ = await ContinueLoad.Reader.ReadAsync(cancellationToken);
        }

        return LoadException is not null
            ? throw LoadException
            : Option<TokenSet>.FromNullable(Loaded);
    }

    public async Task SaveAsync(string path, TokenSet tokenSet, CancellationToken cancellationToken)
    {
        SaveCalls++;
        await Task.Yield();
        if (SaveStarted is not null)
        {
            await SaveStarted.Writer.WriteAsync(true, cancellationToken);
        }

        if (ContinueSave is not null)
        {
            _ = await ContinueSave.Reader.ReadAsync(cancellationToken);
        }

        if (SaveException is not null)
        {
            throw SaveException;
        }

        Loaded = tokenSet;
        Saved = tokenSet;
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        DeleteCalls++;
        await Task.Yield();
        if (DeleteStarted is not null)
        {
            await DeleteStarted.Writer.WriteAsync(true, cancellationToken);
        }

        if (ContinueDelete is not null)
        {
            _ = await ContinueDelete.Reader.ReadAsync(cancellationToken);
        }

        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        Loaded = null;
    }
}
