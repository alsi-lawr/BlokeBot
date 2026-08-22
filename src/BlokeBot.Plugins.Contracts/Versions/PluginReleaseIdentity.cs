using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginReleaseIdentity(SemanticVersion DeclaredVersion, PluginGitTag Tag);

public sealed record PluginInstallationIdentity(PluginId PluginId, PluginReleaseIdentity Release);

[JsonConverter(typeof(PluginGitTagJsonConverter))]
public sealed record PluginGitTag
{
    private PluginGitTag(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? candidate, out PluginGitTag tag)
    {
        tag = null!;
        if (candidate is null or { Length: < 1 or > 128 } || LooksLikeCommitSha(candidate))
        {
            return false;
        }

        if (
            candidate[0] is '.' or '/'
            || candidate[^1] is '.' or '/'
            || candidate.Contains("..", StringComparison.Ordinal)
            || candidate.Contains("//", StringComparison.Ordinal)
            || candidate.Contains("@{", StringComparison.Ordinal)
            || candidate == "@"
            || candidate
                .Split('/')
                .Any(component =>
                    component[0] == '.'
                    || component.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                )
        )
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (character is <= ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\')
            {
                return false;
            }
        }

        tag = new(candidate);
        return true;
    }

    public override string ToString() => Value;

    private static bool LooksLikeCommitSha(string candidate) =>
        candidate.Length is >= 7 and <= 64 && candidate.All(Uri.IsHexDigit);
}

internal sealed class PluginGitTagJsonConverter : JsonConverter<PluginGitTag>
{
    public override PluginGitTag Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var candidate = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return PluginGitTag.TryCreate(candidate, out var tag)
            ? tag
            : throw new JsonException("Invalid mutable Git tag.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        PluginGitTag value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);
}
