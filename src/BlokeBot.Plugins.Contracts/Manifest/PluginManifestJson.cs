using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public static class PluginManifestJson
{
    private static readonly JsonSerializerOptions _options = CreateOptions();

    public static PluginManifestValidationOutcome Validate(
        ReadOnlyMemory<byte> utf8Json,
        PluginHostCompatibilityTarget target
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        if (utf8Json.Length > PluginContractLimits.MaximumManifestBytes)
        {
            return Rejected(PluginManifestErrorCode.ManifestTooLarge, "$");
        }

        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(utf8Json.Span, _options);
        }
        catch (JsonException)
        {
            return Rejected(PluginManifestErrorCode.MalformedJson, "$");
        }

        return manifest is null
            ? Rejected(PluginManifestErrorCode.MalformedJson, "$")
            : PluginManifestValidator.Validate(manifest, target);
    }

    public static async ValueTask<PluginManifestValidationOutcome> ValidateUnboundedAsync(
        Stream utf8Json,
        PluginHostCompatibilityTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        ArgumentNullException.ThrowIfNull(target);
        PluginManifest? manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<PluginManifest>(
                utf8Json,
                _options,
                cancellationToken
            );
        }
        catch (JsonException)
        {
            return Rejected(PluginManifestErrorCode.MalformedJson, "$");
        }

        return manifest is null
            ? Rejected(PluginManifestErrorCode.MalformedJson, "$")
            : PluginManifestValidator.Validate(manifest, target);
    }

    public static byte[] Serialize(ValidatedPluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest.Manifest, _options);
    }

    private static PluginManifestValidationOutcome Rejected(
        PluginManifestErrorCode code,
        string location
    ) =>
        new PluginManifestValidationOutcome.Rejected(
            Array.AsReadOnly([new PluginManifestError(code, location)])
        );

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowDuplicateProperties = false,
            AllowTrailingCommas = false,
            MaxDepth = PluginContractLimits.MaximumPluginValueDepth + 16,
            NumberHandling = JsonNumberHandling.Strict,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        );
        return options;
    }
}
