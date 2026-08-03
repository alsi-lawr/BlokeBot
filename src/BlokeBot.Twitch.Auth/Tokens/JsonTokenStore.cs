using System.Text.Json;
using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Stores Twitch token sets as JSON files.
/// </summary>
public sealed class JsonTokenStore : ITokenStore
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates a JSON token store.
    /// </summary>
    public JsonTokenStore() { }

    /// <inheritdoc />
    public async Task<Option<TokenSet>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return Option<TokenSet>.None;
        }

        await using var stream = File.OpenRead(path);
        var tokenSet =
            await JsonSerializer.DeserializeAsync<TokenSet>(stream, _jsonOpts, cancellationToken)
            ?? throw new JsonException("The Twitch token file did not contain a token set.");
        return Option<TokenSet>.Some(tokenSet);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string path, TokenSet tokenSet, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            directory ?? Directory.GetCurrentDirectory(),
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp"
        );
        try
        {
            await using (
                var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write)
            )
            {
                await JsonSerializer.SerializeAsync(stream, tokenSet, _jsonOpts, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
