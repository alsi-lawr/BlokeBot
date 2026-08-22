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
        var errors = new List<PluginManifestError>();

        ValidateName(manifest.Name, "$.name", errors);
        ValidateText(manifest.Description, "$.description", required: true, errors);
        ValidateCompatibilityRanges(manifest.Compatibility, errors);
        ValidateModulesAssetsAndPayloads(manifest, errors);
        ValidateSettingsAndFeatures(manifest, errors);
        ValidateHostModulesAndMigrations(manifest, errors);
        ValidateAutomations(manifest, errors);
        ValidatePages(manifest, errors);

        if (
            PluginCompatibilityEvaluator.Evaluate(manifest, target)
            is PluginCompatibilityOutcome.Incompatible incompatible
        )
        {
            var incompatibleTargetLocations = incompatible
                .Failures.Where(failure =>
                    failure.Code == PluginCompatibilityFailureCode.IncompatiblePayloadTarget
                )
                .Select(failure =>
                    manifest.Assets.Any(asset => asset.Path == failure.Subject)
                        ? "$.assets.runtimeIdentifiers"
                        : "$.payloads.runtimeIdentifiers"
                )
                .Distinct(StringComparer.Ordinal);
            foreach (var location in incompatibleTargetLocations)
            {
                errors.Add(new(PluginManifestErrorCode.IncompatiblePayloadTarget, location));
            }

            if (
                incompatible.Failures.Any(failure =>
                    failure.Code != PluginCompatibilityFailureCode.IncompatiblePayloadTarget
                )
            )
            {
                errors.Add(new(PluginManifestErrorCode.IncompatibleDeclaration, "$.compatibility"));
            }
        }

        return errors.Count == 0
            ? new PluginManifestValidationOutcome.Accepted(new(manifest))
            : new PluginManifestValidationOutcome.Rejected(errors.AsReadOnly());
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
        )
        {
            errors.Add(new(PluginManifestErrorCode.InvalidCompatibilityRange, "$.compatibility"));
        }
    }

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
