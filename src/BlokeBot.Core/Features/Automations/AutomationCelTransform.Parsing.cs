using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

internal static partial class AutomationCelTransform
{
    private static bool TryParseInput(JsonElement json, out AutomationCelTransformInput input)
    {
        input = null!;
        if (
            !TryString(json, "port-id", out var portId)
            || !TryString(json, "cel-identifier", out var identifier)
            || !TryString(json, "display-name", out var displayName)
            || !TryString(json, "binding-field-id", out var bindingFieldId)
            || !TryType(json, out var valueType)
            || !TryNullability(json, out var nullability)
            || !json.TryGetProperty("fixed", out var fixedJson)
            || !TryValue(fixedJson, valueType, nullability, out var fixedValue)
        )
        {
            return false;
        }

        input = new(
            new(portId),
            new(identifier),
            displayName,
            new(bindingFieldId),
            valueType,
            nullability,
            fixedValue
        );
        return true;
    }

    private static bool TryParseOutput(JsonElement json, out AutomationCelTransformOutput output)
    {
        output = null!;
        if (
            !TryString(json, "port-id", out var portId)
            || !TryString(json, "display-name", out var displayName)
            || !TryType(json, out var valueType)
            || !TryNullability(json, out var nullability)
            || !TryString(json, "cel", out var source)
        )
        {
            return false;
        }

        output = new(new(portId), displayName, valueType, nullability, source);
        return true;
    }

    internal static bool TryValue(
        JsonElement json,
        AutomationPortValueType type,
        AutomationPortNullability nullability,
        out AutomationValue value
    )
    {
        value = null!;
        if (json.ValueKind == JsonValueKind.Null)
        {
            if (nullability != AutomationPortNullability.Nullable)
            {
                return false;
            }

            value = new AutomationValue.Null(type);
            return true;
        }

        AutomationValue? parsed = type switch
        {
            AutomationPortValueType.Text when json.ValueKind == JsonValueKind.String =>
                new AutomationValue.Text(json.GetString()!),
            AutomationPortValueType.Number when json.TryGetDecimal(out var number) =>
                new AutomationValue.Number(number),
            AutomationPortValueType.Boolean
                when json.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                new AutomationValue.Boolean(json.GetBoolean()),
            AutomationPortValueType.Timestamp
                when json.ValueKind == JsonValueKind.String
                    && json.TryGetDateTimeOffset(out var timestamp) =>
                new AutomationValue.Timestamp(timestamp),
            AutomationPortValueType.Actor => TryActor(json),
            AutomationPortValueType.Channel => TryChannel(json),
            AutomationPortValueType.Stream => TryStream(json),
            AutomationPortValueType.Arguments => TryArguments(json),
            _ => null,
        };
        if (parsed is null)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static AutomationValue? TryActor(JsonElement json) =>
        TryString(json, "login", out var login)
        && TryString(json, "display-name", out var displayName)
            ? new AutomationValue.Actor(new(login, displayName))
            : null;

    private static AutomationValue? TryChannel(JsonElement json) =>
        TryString(json, "login", out var login)
        && TryString(json, "display-name", out var displayName)
            ? new AutomationValue.Channel(new(login, displayName))
            : null;

    private static AutomationValue? TryStream(JsonElement json) =>
        json.ValueKind != JsonValueKind.Object
        || !TryOptionalString(json, "title", out var title)
        || !TryOptionalString(json, "game-name", out var gameName)
        || !TryOptionalTimestamp(json, "started-at", out var startedAt)
            ? null
            : new AutomationValue.Stream(new(title, gameName, startedAt));

    private static AutomationValue? TryArguments(JsonElement json) =>
        json.ValueKind == JsonValueKind.Array
        && json.EnumerateArray().All(static value => value.ValueKind == JsonValueKind.String)
            ? new AutomationValue.Arguments([
                .. json.EnumerateArray()
                    .Select(
                        static (value, position) =>
                            new AutomationValueArgument(
                                position,
                                value.GetString()!,
                                [AutomationValueProvenance.Generated]
                            )
                    ),
            ])
            : null;

    private static bool TryOptionalString(JsonElement json, string name, out string? result)
    {
        result = null;
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = value.GetString();
        return true;
    }

    private static bool TryOptionalTimestamp(
        JsonElement json,
        string name,
        out DateTimeOffset? result
    )
    {
        result = null;
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (
            value.ValueKind != JsonValueKind.String
            || !value.TryGetDateTimeOffset(out var timestamp)
        )
        {
            return false;
        }

        result = timestamp;
        return true;
    }

    private static bool TryString(JsonElement json, string name, out string value)
    {
        value = string.Empty;
        if (
            json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static bool TryType(JsonElement json, out AutomationPortValueType value) =>
        TryEnum(json, "type", out value);

    private static bool TryNullability(JsonElement json, out AutomationPortNullability value) =>
        TryEnum(json, "nullability", out value);

    private static bool TryEnum<T>(JsonElement json, string name, out T value)
        where T : struct, Enum
    {
        value = default;
        return TryString(json, name, out var text)
            && Enum.TryParse(text, out value)
            && Enum.IsDefined(value)
            && text == value.ToString();
    }

    private static bool HasDuplicates(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Count() != values.Count();

    private static bool Scalar(AutomationPortValueType type) =>
        type
            is AutomationPortValueType.Text
                or AutomationPortValueType.Number
                or AutomationPortValueType.Boolean
                or AutomationPortValueType.Timestamp;

    private static bool Matches(
        AutomationPortValueType type,
        AutomationPortNullability nullability,
        AutomationValue value
    ) =>
        value switch
        {
            AutomationValue.Null nullValue => nullability == AutomationPortNullability.Nullable
                && nullValue.ValueType == type,
            _ => AutomationPureHandlerRegistry.ValueType(value) == type,
        };

    private static AutomationConfigurationParseResult Invalid(string fieldId, string message) =>
        new AutomationConfigurationParseResult.Invalid([
            new(new AutomationValidationTarget.Field(new(fieldId)), message),
        ]);

    private static AutomationValidationResult InvalidResult(string fieldId, string message) =>
        AutomationValidationResult.Invalid(
            new AutomationValidationTarget.Field(new(fieldId)),
            message
        );
}
