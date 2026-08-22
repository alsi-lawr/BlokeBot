using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.Automations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationCelTransformDocument(
    [property: JsonRequired, JsonPropertyName(AutomationCelTransformDocumentFields.Inputs)]
        IReadOnlyList<AutomationCelTransformInputDocument> Inputs,
    [property: JsonRequired, JsonPropertyName(AutomationCelTransformDocumentFields.Outputs)]
        IReadOnlyList<AutomationCelTransformOutputDocument> Outputs
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationCelTransformInputDocument(
    [property: JsonRequired, JsonPropertyName("port-id")] string PortId,
    [property: JsonRequired, JsonPropertyName("cel-identifier")] string Identifier,
    [property: JsonRequired, JsonPropertyName("display-name")] string DisplayName,
    [property: JsonRequired, JsonPropertyName("binding-field-id")] string BindingFieldId,
    [property: JsonRequired, JsonPropertyName(AutomationCelTransformDocumentFields.ValueType)]
        string ValueType,
    [property: JsonRequired, JsonPropertyName("nullability")] string Nullability,
    [property: JsonRequired, JsonPropertyName(AutomationCelTransformDocumentFields.FixedValue)]
        JsonElement FixedValue
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationCelTransformOutputDocument(
    [property: JsonRequired, JsonPropertyName("port-id")] string PortId,
    [property: JsonRequired, JsonPropertyName("display-name")] string DisplayName,
    [property: JsonRequired, JsonPropertyName("type")] string ValueType,
    [property: JsonRequired, JsonPropertyName("nullability")] string Nullability,
    [property: JsonRequired, JsonPropertyName("cel")] string Source
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationCelIdentityDocument(
    [property: JsonRequired] string Login,
    [property: JsonRequired, JsonPropertyName("display-name")] string DisplayName
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationCelStreamDocument(
    string? Title,
    [property: JsonPropertyName("game-name")] string? GameName,
    [property: JsonPropertyName("started-at")] DateTimeOffset? StartedAt
);

internal static class AutomationCelTransformDocumentSerializer
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static bool TryDeserialize<T>(JsonElement json, [NotNullWhen(true)] out T? document)
        where T : class
    {
        try
        {
            document = json.Deserialize<T>(_options);
            return document is not null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            document = null;
            return false;
        }
    }

    internal static JsonElement Serialize<T>(T document)
        where T : class => JsonSerializer.SerializeToElement(document, _options);
}

internal static class AutomationCelTransformDocumentFields
{
    internal const string Inputs = "inputs";
    internal const string Outputs = "outputs";
    internal const string ValueType = "type";
    internal const string FixedValue = "fixed";
    internal const string IdentityLogin = "login";
    internal const string IdentityDisplayName = "display-name";
}
