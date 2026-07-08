using System.Text.Json;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Stores Twitch token sets as JSON files.
/// </summary>
public sealed class JsonTwitchTokenStore : ITwitchTokenStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates a JSON token store.
    /// </summary>
    public JsonTwitchTokenStore() { }

    /// <inheritdoc />
    public async Task<TwitchTokenSet?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TwitchTokenSet>(
            stream,
            JsonOpts,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string path,
        TwitchTokenSet tokenSet,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, tokenSet, JsonOpts, cancellationToken);
    }
}
