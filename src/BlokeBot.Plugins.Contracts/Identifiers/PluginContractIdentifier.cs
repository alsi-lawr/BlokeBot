using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public abstract record PluginContractIdentifier
{
    private protected PluginContractIdentifier(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;
}

internal interface IPluginContractIdentifier<TIdentifier>
    where TIdentifier : PluginContractIdentifier
{
    static abstract bool TryCreate(string? candidate, out TIdentifier identifier);
}

internal static class PluginContractIdentifierSyntax
{
    internal static bool TryCreate<TIdentifier>(
        string? candidate,
        Func<string, TIdentifier> create,
        out TIdentifier identifier
    )
        where TIdentifier : PluginContractIdentifier
    {
        var valid = IsValid(candidate);
        identifier = valid ? create(candidate!) : null!;
        return valid;
    }

    internal static bool IsValid(string? candidate)
    {
        if (candidate is null or { Length: < 1 or > 64 })
        {
            return false;
        }

        if (!IsLowerAsciiLetter(candidate[0]) || !IsLowerAlphaNumeric(candidate[^1]))
        {
            return false;
        }

        var previousWasSeparator = false;
        foreach (var character in candidate)
        {
            var isSeparator = character is '.' or '-' or '_';
            if (!IsLowerAlphaNumeric(character) && !isSeparator)
            {
                return false;
            }

            if (isSeparator && previousWasSeparator)
            {
                return false;
            }

            previousWasSeparator = isSeparator;
        }

        return true;
    }

    private static bool IsLowerAlphaNumeric(char character) =>
        character is (>= 'a' and <= 'z') or (>= '0' and <= '9');

    private static bool IsLowerAsciiLetter(char character) => character is >= 'a' and <= 'z';
}

internal sealed class PluginContractIdentifierJsonConverter<TIdentifier>
    : JsonConverter<TIdentifier>
    where TIdentifier : PluginContractIdentifier, IPluginContractIdentifier<TIdentifier>
{
    public override TIdentifier Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var candidate = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return TIdentifier.TryCreate(candidate, out var identifier)
            ? identifier
            : throw new JsonException($"Invalid {typeof(TIdentifier).Name} value.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        TIdentifier value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);
}
