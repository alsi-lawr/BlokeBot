using System.Text.Json;

public sealed record TokenState(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc
);

public interface ITokenCache
{
    TokenState? Load(string path);
    void Save(string path, TokenState state);
}

public sealed class JsonTokenCache : ITokenCache
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public TokenState? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TokenState>(json, JsonOpts);
    }

    public void Save(string path, TokenState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOpts);
        File.WriteAllText(path, json);
    }
}
