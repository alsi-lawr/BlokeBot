using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public static partial class PluginManifestValidator
{
    private static readonly HashSet<string> _browserMediaTypes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "text/html",
        "text/css",
        "text/javascript",
        "application/javascript",
        "application/json",
        "image/svg+xml",
        "font/woff2",
    };

    private static readonly HashSet<string> _mediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "image/avif",
        "audio/mpeg",
        "audio/ogg",
        "audio/wav",
        "video/mp4",
        "video/webm",
    };

    private static void ValidateModulesAssetsAndPayloads(
        PluginManifest manifest,
        List<PluginManifestError> errors
    )
    {
        ValidateCount(manifest.LuaModules, "$.luaModules", errors);
        ValidateCount(manifest.Assets, "$.assets", errors);
        ValidateCount(manifest.Payloads, "$.payloads", errors);
        ValidateDistinct(manifest.LuaModules.Select(module => module.Id), "$.luaModules", errors);
        ValidateDistinct(manifest.Assets.Select(asset => asset.Id), "$.assets", errors);
        ValidateDistinct(manifest.Payloads.Select(payload => payload.Id), "$.payloads", errors);

        foreach (var module in manifest.LuaModules)
        {
            if (
                !PluginPackagePath.IsValid(module.Path)
                || !module.Path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            )
            {
                errors.Add(new(PluginManifestErrorCode.InvalidLuaModule, "$.luaModules"));
            }
        }

        if (!manifest.LuaModules.Any(module => module.Id == manifest.EntryModule))
        {
            errors.Add(new(PluginManifestErrorCode.InvalidLuaModule, "$.entryModule"));
        }

        foreach (var asset in manifest.Assets)
        {
            if (!ValidAsset(asset))
            {
                errors.Add(new(PluginManifestErrorCode.InvalidAsset, "$.assets"));
            }
        }

        foreach (var payload in manifest.Payloads)
        {
            if (!ValidPayload(payload))
            {
                errors.Add(new(PluginManifestErrorCode.InvalidPayload, "$.payloads"));
            }
        }

        ValidateDeclaredPaths(manifest, errors);
    }

    private static bool ValidAsset(PluginAssetDescriptor asset) =>
        PluginPackagePath.IsValid(asset.Path)
        && IsValidText(asset.Purpose, required: true)
        && ValidRuntimeIdentifiers(asset.RuntimeIdentifiers)
        && asset.MaximumBytes > 0
        && asset.Kind switch
        {
            PluginAssetKind.Browser => asset.MaximumBytes
                <= PluginContractLimits.MaximumBrowserAssetBytes
                && _browserMediaTypes.Contains(asset.MediaType)
                && AssetExtensionMatches(asset),
            PluginAssetKind.Media => asset.MaximumBytes
                <= PluginContractLimits.MaximumMediaAssetBytes
                && _mediaTypes.Contains(asset.MediaType)
                && AssetExtensionMatches(asset),
        };

    private static bool ValidPayload(PluginPayloadDescriptor payload) =>
        PluginPackagePath.IsValid(payload.Path)
        && IsValidText(payload.Purpose, required: true)
        && ValidRuntimeIdentifiers(payload.RuntimeIdentifiers)
        && payload.MaximumBytes is > 0 and <= PluginContractLimits.MaximumDeclaredPayloadBytes;

    private static bool ValidRuntimeIdentifiers(
        ImmutableArray<PluginRuntimeIdentifier> runtimeIdentifiers
    ) =>
        !runtimeIdentifiers.IsDefaultOrEmpty
        && runtimeIdentifiers.Length <= PluginContractLimits.MaximumDeclarationsPerSurface
        && runtimeIdentifiers.All(Enum.IsDefined)
        && runtimeIdentifiers.Distinct().Count() == runtimeIdentifiers.Length;

    private static bool AssetExtensionMatches(PluginAssetDescriptor asset)
    {
        var extension = Path.GetExtension(asset.Path);
        return asset.MediaType.ToLowerInvariant() switch
        {
            "text/html" => extension is ".html" or ".htm",
            "text/css" => extension == ".css",
            "text/javascript" or "application/javascript" => extension is ".js" or ".mjs",
            "application/json" => extension == ".json",
            "image/svg+xml" => extension == ".svg",
            "font/woff2" => extension == ".woff2",
            "image/png" => extension == ".png",
            "image/jpeg" => extension is ".jpg" or ".jpeg",
            "image/gif" => extension == ".gif",
            "image/webp" => extension == ".webp",
            "image/avif" => extension == ".avif",
            "audio/mpeg" => extension is ".mp3" or ".mpeg",
            "audio/ogg" => extension is ".ogg" or ".oga",
            "audio/wav" => extension == ".wav",
            "video/mp4" => extension == ".mp4",
            "video/webm" => extension == ".webm",
            _ => false,
        };
    }

    private static void ValidateDeclaredPaths(
        PluginManifest manifest,
        List<PluginManifestError> errors
    )
    {
        var paths = manifest
            .LuaModules.Select(module => module.Path)
            .Concat(manifest.Assets.Select(asset => asset.Path))
            .Concat(manifest.Payloads.Select(payload => payload.Path))
            .Prepend(PluginPackage.ManifestPath)
            .ToArray();
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!PluginPackagePath.TryCanonicalize(path, out var canonicalPath))
            {
                continue;
            }

            if (!exact.Add(canonicalPath))
            {
                errors.Add(new(PluginManifestErrorCode.DuplicatePath, path));
            }
            else if (!folded.Add(canonicalPath))
            {
                errors.Add(new(PluginManifestErrorCode.CaseCollidingPath, path));
            }
        }
    }

    private static void ValidateSettingsAndFeatures(
        PluginManifest manifest,
        List<PluginManifestError> errors
    )
    {
        ValidateCount(manifest.Settings, "$.settings", errors);
        ValidateCount(manifest.Features, "$.features", errors);
        ValidateDistinct(manifest.Settings.Select(setting => setting.Id), "$.settings", errors);
        ValidateDistinct(manifest.Features.Select(feature => feature.Id), "$.features", errors);
        var settingIds = manifest.Settings.Select(setting => setting.Id).ToHashSet();
        var templateIds = manifest.AutomationTemplates.Select(template => template.Id).ToHashSet();

        foreach (var setting in manifest.Settings)
        {
            ValidateName(setting.Name, "$.settings.name", errors);
            ValidateText(setting.Description, "$.settings.description", required: true, errors);
            if (!ValidSettingSchema(setting.Schema))
            {
                errors.Add(new(PluginManifestErrorCode.InvalidSetting, "$.settings"));
            }
        }

        foreach (var feature in manifest.Features)
        {
            ValidateName(feature.Name, "$.features.name", errors);
            ValidateText(feature.Description, "$.features.description", required: true, errors);
            if (
                feature.Settings.Any(id => !settingIds.Contains(id))
                || feature.AutomationTemplates.Any(id => !templateIds.Contains(id))
                || feature.Settings.Distinct().Count() != feature.Settings.Length
                || feature.AutomationTemplates.Distinct().Count()
                    != feature.AutomationTemplates.Length
            )
            {
                errors.Add(new(PluginManifestErrorCode.InvalidFeature, "$.features"));
            }

            ValidateTwitch(feature.Twitch, errors);
            ValidateDispatchDeclarations(manifest, feature, errors);
        }
    }

    private static bool ValidSettingSchema(PluginSettingSchema schema) =>
        schema.Match(
            _ => true,
            text =>
                text.MaximumLength is >= 1 and <= PluginContractLimits.MaximumTextSettingCharacters,
            multiline =>
                multiline.MaximumLength
                    is >= 1
                        and <= PluginContractLimits.MaximumMultilineSettingCharacters,
            integer =>
                integer.Minimum >= PluginContractLimits.MinimumIntegerSettingValue
                && integer.Maximum <= PluginContractLimits.MaximumIntegerSettingValue
                && integer.Minimum <= integer.Maximum,
            number =>
                number.Minimum >= PluginContractLimits.MinimumNumberSettingValue
                && number.Maximum <= PluginContractLimits.MaximumNumberSettingValue
                && number.Minimum <= number.Maximum
                && number.DecimalPlaces
                    is >= 0
                        and <= PluginContractLimits.MaximumNumberDecimalPlaces,
            duration =>
                duration.MinimumSeconds >= 0
                && duration.MaximumSeconds <= PluginContractLimits.MaximumDurationSettingSeconds
                && duration.MinimumSeconds <= duration.MaximumSeconds,
            choice => ValidChoices(choice.Choices),
            secret =>
                secret.MaximumLength
                    is >= 1
                        and <= PluginContractLimits.MaximumSecretSettingCharacters
        );

    private static bool ValidChoices(ImmutableArray<PluginSettingChoiceDescriptor> choices) =>
        choices.Length is >= 2 and <= PluginContractLimits.MaximumSettingChoices
        && choices.Select(static choice => choice.Id).Distinct().Count() == choices.Length
        && choices.All(static choice =>
            !string.IsNullOrWhiteSpace(choice.Name)
            && choice.Name.Length <= PluginContractLimits.MaximumNameCharacters
        );

    private static void ValidateTwitch(
        PluginTwitchRequirements twitch,
        List<PluginManifestError> errors
    )
    {
        var scopes = twitch.Scopes.ToArray();
        var eventTypes = twitch.EventSubTypes.ToArray();
        var valid =
            scopes.Length <= PluginContractLimits.MaximumDeclarationsPerSurface
            && eventTypes.Length <= PluginContractLimits.MaximumDeclarationsPerSurface
            && scopes.Distinct(StringComparer.Ordinal).Count() == scopes.Length
            && eventTypes.Distinct(StringComparer.Ordinal).Count() == eventTypes.Length
            && scopes.All(ValidTwitchScope)
            && eventTypes.All(ValidEventSubType)
            && scopes.All(scope => scope == scope.Trim().ToLowerInvariant())
            && eventTypes.All(eventType => eventType == eventType.ToLowerInvariant());
        if (!valid)
        {
            errors.Add(new(PluginManifestErrorCode.InvalidTwitchDeclaration, "$.features.twitch"));
        }
    }

    private static bool ValidTwitchScope(string scope) =>
        scope is { Length: >= 1 and <= 128 }
        && scope.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '_');

    private static bool ValidEventSubType(string eventType) =>
        eventType is { Length: >= 1 and <= 160 }
        && eventType.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
        );
}
