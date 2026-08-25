using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public static partial class PluginPageDocumentParser
{
    private static Dictionary<string, PluginValue> Fields(PluginValue.Map map) =>
        map.Properties.ToDictionary(
            static property => property.Name,
            static property => property.Value,
            StringComparer.Ordinal
        );

    private static bool String(
        IReadOnlyDictionary<string, PluginValue> fields,
        string name,
        out string value
    )
    {
        var valid =
            fields.TryGetValue(name, out var candidate)
            && candidate
                is PluginValue.String
                {
                    Value.Length: > 0 and <= PluginContractLimits.MaximumDescriptionCharacters,
                };
        value = valid ? ((PluginValue.String)candidate!).Value : string.Empty;
        return valid;
    }

    private static bool OptionalString(
        IReadOnlyDictionary<string, PluginValue> fields,
        string name,
        out string? value
    )
    {
        if (!fields.TryGetValue(name, out var candidate) || candidate is PluginValue.Nil)
        {
            value = null;
            return true;
        }
        value = candidate
            is PluginValue.String
            {
                Value.Length: <= PluginContractLimits.MaximumDescriptionCharacters,
            } text
            ? text.Value
            : null;
        return value is not null;
    }

    private static bool Integer(
        IReadOnlyDictionary<string, PluginValue> fields,
        string name,
        out int value
    )
    {
        var valid =
            fields.TryGetValue(name, out var candidate)
            && candidate is PluginValue.Number number
            && number.Value == Math.Truncate(number.Value)
            && number.Value is >= int.MinValue and <= int.MaxValue;
        value = valid ? (int)((PluginValue.Number)candidate!).Value : 0;
        return valid;
    }

    private static bool Array(
        IReadOnlyDictionary<string, PluginValue> fields,
        string name,
        out ImmutableArray<PluginValue> values
    )
    {
        var valid = fields.TryGetValue(name, out var candidate) && candidate is PluginValue.Array;
        values = valid ? ((PluginValue.Array)candidate!).Items : [];
        return valid;
    }

    private static bool OptionalBoolean(
        IReadOnlyDictionary<string, PluginValue> fields,
        string name,
        out bool value
    )
    {
        if (!fields.TryGetValue(name, out var candidate) || candidate is PluginValue.Nil)
        {
            value = false;
            return true;
        }
        value = candidate is PluginValue.Boolean boolean && boolean.Value;
        return candidate is PluginValue.Boolean;
    }

    private static bool TryTone(string value, out PluginPageStatusTone tone) =>
        Enum.TryParse(value, ignoreCase: true, out tone);

    private static bool TryFieldKind(string value, out PluginPageFieldKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind);

    private static bool ValidLocalId(string value) =>
        value.Length <= 64
        && value[0] is >= 'a' and <= 'z'
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static T? Invalid<T>(string location, List<PluginPageDocumentError> errors)
        where T : class
    {
        errors.Add(new(PluginPageDocumentErrorCode.InvalidSchema, location));
        return null;
    }

    private static PluginPageDocumentParseOutcome Rejected(
        PluginPageDocumentErrorCode code,
        string location,
        List<PluginPageDocumentError> errors
    )
    {
        errors.Add(new(code, location));
        return new PluginPageDocumentParseOutcome.Rejected(errors);
    }
}
