namespace BlokeBot.Plugins.Contracts;

public static partial class PluginManifestValidator
{
    public static PluginManifestValidationOutcome Validate(
        PluginManifest manifest,
        PluginHostCompatibilityTarget target
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(target);
        var errors = ValidateDeclaration(manifest);

        if (
            PluginCompatibilityEvaluator.Evaluate(manifest, target)
            is PluginCompatibilityOutcome.Incompatible
        )
        {
            errors.Add(new(PluginManifestErrorCode.IncompatibleDeclaration, "$.compatibility"));
        }

        return errors.Count == 0
            ? new PluginManifestValidationOutcome.Accepted(new(manifest))
            : new PluginManifestValidationOutcome.Rejected(errors.AsReadOnly());
    }

    internal static PluginManifestDeclarationValidationOutcome ValidateForMarketplace(
        PluginManifest manifest
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = ValidateDeclaration(manifest);
        return errors.Count == 0
            ? new PluginManifestDeclarationValidationOutcome.Accepted(new(manifest))
            : new PluginManifestDeclarationValidationOutcome.Rejected(errors.AsReadOnly());
    }

    private static List<PluginManifestError> ValidateDeclaration(PluginManifest manifest)
    {
        var errors = new List<PluginManifestError>();
        if (manifest.ManifestVersion != PluginRuntimeContract.Current.ManifestVersion)
        {
            errors.Add(new(PluginManifestErrorCode.IncompatibleDeclaration, "$.manifestVersion"));
        }

        ValidateName(manifest.Name, "$.name", errors);
        ValidateText(manifest.Description, "$.description", required: true, errors);
        ValidateMarketplace(manifest.Marketplace, errors);
        ValidateCompatibilityRanges(manifest.Compatibility, errors);
        ValidateReleaseTargets(manifest, errors);
        ValidateModulesAssetsAndPayloads(manifest, errors);
        ValidateSettingsAndFeatures(manifest, errors);
        ValidateHostModulesAndMigrations(manifest, errors);
        ValidateAutomations(manifest, errors);
        ValidatePages(manifest, errors);
        ValidatePageActionHandlerContracts(manifest, errors);
        return errors;
    }

    private static void ValidateMarketplace(
        PluginMarketplaceMetadata marketplace,
        List<PluginManifestError> errors
    )
    {
        if (
            marketplace is null
            || string.IsNullOrWhiteSpace(marketplace.Author)
            || marketplace.Author.Length > PluginContractLimits.MaximumMarketplaceAuthorCharacters
            || marketplace.Author.Any(char.IsControl)
            || marketplace.Tags.IsDefault
            || marketplace.Tags.Length > PluginContractLimits.MaximumMarketplaceTags
            || marketplace.Tags.Any(tag =>
                string.IsNullOrWhiteSpace(tag)
                || tag.Length > PluginContractLimits.MaximumMarketplaceTagCharacters
                || tag.Any(char.IsControl)
            )
            || marketplace.Tags.Distinct(StringComparer.Ordinal).Count() != marketplace.Tags.Length
            || !ValidHttpsUrl(marketplace.IconUrl, optional: true)
            || marketplace.MediaUrls.IsDefault
            || marketplace.MediaUrls.Length > PluginContractLimits.MaximumMarketplaceMediaUrls
            || marketplace.MediaUrls.Any(url => !ValidHttpsUrl(url, optional: false))
            || marketplace.MediaUrls.Distinct(StringComparer.Ordinal).Count()
                != marketplace.MediaUrls.Length
        )
        {
            errors.Add(new(PluginManifestErrorCode.InvalidMarketplace, "$.marketplace"));
        }
    }

    private static void ValidateCompatibilityRanges(
        PluginCompatibilityDeclaration declaration,
        List<PluginManifestError> errors
    )
    {
        if (
            declaration.MinimumApiVersion.CompareTo(declaration.MaximumApiVersion) > 0
            || declaration.MinimumBlokeBotVersion.CompareTo(
                declaration.MaximumBlokeBotVersionExclusive
            ) >= 0
            || declaration.SupportedTargets.IsDefaultOrEmpty
            || declaration.SupportedTargets.Distinct().Count()
                != declaration.SupportedTargets.Length
            || declaration.LuaVersion != PluginRuntimeContract.Current.LuaVersion
        )
        {
            errors.Add(new(PluginManifestErrorCode.InvalidCompatibilityRange, "$.compatibility"));
        }
    }

    private static void ValidateReleaseTargets(
        PluginManifest manifest,
        List<PluginManifestError> errors
    )
    {
        if (manifest.Compatibility.SupportedTargets.IsDefaultOrEmpty)
        {
            return;
        }

        var declaredTargets = manifest
            .Assets.Select(static asset => asset.RuntimeIdentifiers)
            .Concat(manifest.Payloads.Select(static payload => payload.RuntimeIdentifiers))
            .SelectMany(static targets => targets);
        if (
            declaredTargets.Any(target => !manifest.Compatibility.SupportedTargets.Contains(target))
        )
        {
            errors.Add(
                new(
                    PluginManifestErrorCode.InvalidCompatibilityRange,
                    "$.compatibility.supportedTargets"
                )
            );
        }
    }

    private static bool ValidHttpsUrl(string? value, bool optional) =>
        value is null
            ? optional
            : value.Length <= PluginContractLimits.MaximumMarketplaceUrlCharacters
                && value.StartsWith("https://", StringComparison.Ordinal)
                && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && !string.IsNullOrWhiteSpace(uri.Host)
                && string.IsNullOrEmpty(uri.UserInfo);

    private static void ValidateText(
        string? value,
        string location,
        bool required,
        List<PluginManifestError> errors
    )
    {
        if (!IsValidText(value, required))
        {
            errors.Add(new(PluginManifestErrorCode.InvalidText, location));
        }
    }

    private static bool IsValidText(string? value, bool required) =>
        value is not null
        && (!required || !string.IsNullOrWhiteSpace(value))
        && value.Length <= PluginContractLimits.MaximumDescriptionCharacters
        && !value.Any(char.IsControl);

    private static void ValidateName(
        string? value,
        string location,
        List<PluginManifestError> errors
    )
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > PluginContractLimits.MaximumNameCharacters
            || value.Any(char.IsControl)
        )
        {
            errors.Add(new(PluginManifestErrorCode.InvalidText, location));
        }
    }

    private static void ValidateCount<T>(
        IReadOnlyCollection<T> declarations,
        string location,
        List<PluginManifestError> errors
    )
    {
        if (declarations.Count > PluginContractLimits.MaximumDeclarationsPerSurface)
        {
            errors.Add(new(PluginManifestErrorCode.TooManyDeclarations, location));
        }
    }

    private static void ValidateDistinct<T>(
        IEnumerable<T> values,
        string location,
        List<PluginManifestError> errors
    )
        where T : notnull
    {
        var seen = new HashSet<T>();
        if (values.Any(value => !seen.Add(value)))
        {
            errors.Add(new(PluginManifestErrorCode.DuplicateIdentifier, location));
        }
    }

    private static bool ValidEntryPoint(string? value) =>
        value is { Length: >= 1 and <= 96 }
        && (value[0] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_')
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
