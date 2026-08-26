using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginReleaseIdentity(SemanticVersion DeclaredVersion, PluginGitTag Tag);

public sealed record PluginGitTagSyntaxContract(
    int MinimumLength,
    int MaximumLength,
    int MinimumCommitShaLength,
    int MaximumCommitShaLength
)
{
    public static PluginGitTagSyntaxContract Current { get; } = new(1, 128, 7, 64);
}

public sealed record PluginInstallationIdentity(PluginId PluginId, PluginReleaseIdentity Release);

[JsonConverter(typeof(PluginGitTagJsonConverter))]
[TomlConverter(typeof(PluginGitTagTomlConverter))]
public sealed record PluginGitTag
{
    private PluginGitTag(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? candidate, out PluginGitTag tag)
    {
        tag = null!;
        var contract = PluginGitTagSyntaxContract.Current;
        if (
            candidate is null
            || candidate.Length < contract.MinimumLength
            || candidate.Length > contract.MaximumLength
            || LooksLikeCommitSha(candidate)
        )
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

    private static bool LooksLikeCommitSha(string candidate)
    {
        var contract = PluginGitTagSyntaxContract.Current;
        return candidate.Length >= contract.MinimumCommitShaLength
            && candidate.Length <= contract.MaximumCommitShaLength
            && candidate.All(Uri.IsHexDigit);
    }
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
