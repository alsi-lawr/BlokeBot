using System.Text.Json;

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
    public async Task<TokenSet?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TokenSet>(
            stream,
            _jsonOpts,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task SaveAsync(string path, TokenSet tokenSet, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, tokenSet, _jsonOpts, cancellationToken);
    }
}
