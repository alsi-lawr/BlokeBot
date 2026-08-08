using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

/// <summary>
/// JSON field readers and parse-result constructors shared by the automation catalog modules'
/// configuration parsers.
/// </summary>
internal static class AutomationConfigurationJson
{
    internal static bool TryReadString(JsonElement json, string propertyName, out string value)
    {
        value = string.Empty;
        if (
            json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    internal static bool TryReadInt32(JsonElement json, string propertyName, out int value)
    {
        value = 0;
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out value);
    }

    internal static bool TryReadInt64(JsonElement json, string propertyName, out long value)
    {
        value = 0;
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(propertyName, out var property)
            && property.TryGetInt64(out value);
    }

    internal static AutomationConfigurationParseResult Parsed(
        AutomationConfiguration configuration
    ) => new AutomationConfigurationParseResult.Parsed(configuration);

    internal static AutomationConfigurationParseResult Invalid(string fieldId, string message) =>
        new AutomationConfigurationParseResult.Invalid([
            new(new AutomationValidationTarget.Field(new(fieldId)), message),
        ]);
}
