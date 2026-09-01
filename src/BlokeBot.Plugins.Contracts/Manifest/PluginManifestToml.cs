using System.Text;
using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts;

public static class PluginManifestToml
{
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private static readonly TomlSerializerOptions _options = CreateOptions();

    public static PluginManifestValidationOutcome Validate(
        ReadOnlyMemory<byte> utf8Toml,
        PluginHostCompatibilityTarget target
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        if (utf8Toml.Length > PluginContractLimits.MaximumManifestBytes)
        {
            return Rejected(PluginManifestErrorCode.ManifestTooLarge, "$toml");
        }

        try
        {
            var toml = _utf8.GetString(utf8Toml.Span);
            var document = TomlSerializer.Deserialize<TomlTable>(toml, _options);
            if (document is null || !PluginManifestTomlShape.HasOnlyKnownFields(document))
            {
                return Rejected(PluginManifestErrorCode.MalformedToml, "$toml");
            }

            var manifest = TomlSerializer.Deserialize<PluginManifest>(toml, _options);
            return manifest is null
                ? Rejected(PluginManifestErrorCode.MalformedToml, "$toml")
                : PluginManifestValidator.Validate(manifest, target);
        }
        catch (Exception exception) when (exception is TomlException or DecoderFallbackException)
        {
            return Rejected(PluginManifestErrorCode.MalformedToml, "$toml");
        }
    }

    internal static PluginManifestDeclarationValidationOutcome ValidateForMarketplace(
        ReadOnlyMemory<byte> utf8Toml
    )
    {
        if (utf8Toml.Length > PluginContractLimits.MaximumManifestBytes)
        {
            return DeclarationRejected(PluginManifestErrorCode.ManifestTooLarge, "$toml");
        }

        try
        {
            var toml = _utf8.GetString(utf8Toml.Span);
            var document = TomlSerializer.Deserialize<TomlTable>(toml, _options);
            if (document is null || !PluginManifestTomlShape.HasOnlyKnownFields(document))
            {
                return DeclarationRejected(PluginManifestErrorCode.MalformedToml, "$toml");
            }

            var manifest = TomlSerializer.Deserialize<PluginManifest>(toml, _options);
            return manifest is null
                ? DeclarationRejected(PluginManifestErrorCode.MalformedToml, "$toml")
                : PluginManifestValidator.ValidateForMarketplace(manifest);
        }
        catch (Exception exception) when (exception is TomlException or DecoderFallbackException)
        {
            return DeclarationRejected(PluginManifestErrorCode.MalformedToml, "$toml");
        }
    }

    public static async ValueTask<PluginManifestValidationOutcome> ValidateAsync(
        Stream utf8Toml,
        PluginHostCompatibilityTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(utf8Toml);
        ArgumentNullException.ThrowIfNull(target);
        using var content = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (content.Length <= PluginContractLimits.MaximumManifestBytes)
        {
            var read = await utf8Toml.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return Validate(content.ToArray(), target);
            }

            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return Rejected(PluginManifestErrorCode.ManifestTooLarge, "$toml");
    }

    public static byte[] Serialize(ValidatedPluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return _utf8.GetBytes(TomlSerializer.Serialize(manifest.Manifest, _options));
    }

    private static PluginManifestValidationOutcome Rejected(
        PluginManifestErrorCode code,
        string location
    ) =>
        new PluginManifestValidationOutcome.Rejected(
            Array.AsReadOnly([new PluginManifestError(code, location)])
        );

    private static PluginManifestDeclarationValidationOutcome DeclarationRejected(
        PluginManifestErrorCode code,
        string location
    ) =>
        new PluginManifestDeclarationValidationOutcome.Rejected(
            Array.AsReadOnly([new PluginManifestError(code, location)])
        );

    private static TomlSerializerOptions CreateOptions() =>
        new()
        {
            Converters =
            [
                new PluginPageActionInputsTomlConverter(),
                new PluginContractIdentifierTomlConverter<PluginId>(),
                new PluginContractIdentifierTomlConverter<PluginSettingId>(),
                new PluginContractIdentifierTomlConverter<PluginSettingChoiceId>(),
                new PluginContractIdentifierTomlConverter<PluginFeatureId>(),
                new PluginContractIdentifierTomlConverter<PluginEventHandlerId>(),
                new PluginContractIdentifierTomlConverter<PluginScheduleHandlerId>(),
                new PluginContractIdentifierTomlConverter<PluginWebhookId>(),
                new PluginContractIdentifierTomlConverter<PluginActionId>(),
                new PluginContractIdentifierTomlConverter<PluginPageActionInputId>(),
                new PluginContractIdentifierTomlConverter<PluginMigrationId>(),
                new PluginContractIdentifierTomlConverter<PluginLuaModuleId>(),
                new PluginContractIdentifierTomlConverter<PluginAutomationDefinitionId>(),
                new PluginContractIdentifierTomlConverter<PluginAutomationTemplateId>(),
                new PluginContractIdentifierTomlConverter<PluginTemplateNodeId>(),
                new PluginContractIdentifierTomlConverter<PluginPageId>(),
                new PluginContractIdentifierTomlConverter<PluginAssetId>(),
                new PluginContractIdentifierTomlConverter<PluginPayloadId>(),
                new PluginContractIdentifierTomlConverter<PluginAutomationFieldId>(),
                new PluginContractIdentifierTomlConverter<PluginHostModuleId>(),
                new PluginContractIdentifierTomlConverter<PluginHostOperationId>(),
                new PluginApiVersionTomlConverter(),
                new PluginGitTagTomlConverter(),
                new SemanticVersionTomlConverter(),
                new PluginManifestEnumTomlConverter<PluginLuaVersion>(),
                new PluginManifestEnumTomlConverter<PluginRuntimeIdentifier>(),
                new PluginManifestEnumTomlConverter<PluginAssetKind>(),
                new PluginManifestEnumTomlConverter<PluginSettingScope>(),
                new PluginManifestEnumTomlConverter<PluginAutomationDefinitionKind>(),
                new PluginManifestEnumTomlConverter<PluginValueKind>(),
                new PluginManifestEnumTomlConverter<PluginTwitchEventKind>(),
                new PluginManifestEnumTomlConverter<PluginBlokeBotEventKind>(),
            ],
            DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingNull,
            DuplicateKeyHandling = TomlDuplicateKeyHandling.Error,
            InlineTablePolicy = TomlInlineTablePolicy.Never,
            MappingOrder = TomlMappingOrderPolicy.Declaration,
            MaxDepth = PluginContractLimits.MaximumPluginValueDepth + 16,
            NewLine = TomlNewLineKind.Lf,
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TableArrayStyle = TomlTableArrayStyle.Headers,
            WriteIndented = true,
        };
}

internal sealed class PluginContractIdentifierTomlConverter<TIdentifier>
    : TomlConverter<TIdentifier>
    where TIdentifier : PluginContractIdentifier, IPluginContractIdentifier<TIdentifier>
{
    public override TIdentifier Read(TomlReader reader) =>
        TIdentifier.TryCreate(reader.GetString(), out var identifier)
            ? identifier
            : throw reader.CreateException($"Invalid {typeof(TIdentifier).Name} value.");

    public override void Write(TomlWriter writer, TIdentifier value) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class PluginApiVersionTomlConverter : TomlConverter<PluginApiVersion>
{
    public override PluginApiVersion Read(TomlReader reader)
    {
        var candidate = reader.GetInt64();
        return
            candidate is >= int.MinValue and <= int.MaxValue
            && PluginApiVersion.TryCreate((int)candidate, out var version)
            ? version
            : throw reader.CreateException("Invalid plugin API version.");
    }

    public override void Write(TomlWriter writer, PluginApiVersion value) =>
        writer.WriteIntegerValue(value.Value);
}

internal sealed class PluginGitTagTomlConverter : TomlConverter<PluginGitTag>
{
    public override PluginGitTag Read(TomlReader reader) =>
        PluginGitTag.TryCreate(reader.GetString(), out var tag)
            ? tag
            : throw reader.CreateException("Invalid Git tag.");

    public override void Write(TomlWriter writer, PluginGitTag value) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class SemanticVersionTomlConverter : TomlConverter<SemanticVersion>
{
    public override SemanticVersion Read(TomlReader reader) =>
        SemanticVersion.TryCreate(reader.GetString(), out var version)
            ? version
            : throw reader.CreateException("Invalid semantic version.");

    public override void Write(TomlWriter writer, SemanticVersion value) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class PluginManifestEnumTomlConverter<TEnum> : TomlConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<string, TEnum> _byName = Enum.GetValues<TEnum>()
        .ToDictionary(SerializedName, static value => value, StringComparer.Ordinal);

    public override TEnum Read(TomlReader reader) =>
        _byName.TryGetValue(reader.GetString(), out var value)
            ? value
            : throw reader.CreateException($"Invalid {typeof(TEnum).Name} value.");

    public override void Write(TomlWriter writer, TEnum value) =>
        writer.WriteStringValue(SerializedName(value));

    private static string SerializedName(TEnum value) =>
        value is PluginRuntimeIdentifier runtimeIdentifier
            ? runtimeIdentifier switch
            {
                PluginRuntimeIdentifier.LinuxX64 => "linux-x64",
                PluginRuntimeIdentifier.LinuxArm64 => "linux-arm64",
                PluginRuntimeIdentifier.MacOsArm64 => "osx-arm64",
                PluginRuntimeIdentifier.WindowsX64 => "win-x64",
                PluginRuntimeIdentifier.WindowsArm64 => "win-arm64",
            }
            : JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
