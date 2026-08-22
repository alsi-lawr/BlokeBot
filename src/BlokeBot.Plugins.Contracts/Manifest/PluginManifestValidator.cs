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
        ValidateModulesAndAssets(manifest, errors);
        ValidateSettingsAndFeatures(manifest, errors);
        ValidateHostModulesAndMigrations(manifest, errors);
        ValidateAutomations(manifest, errors);
        ValidatePages(manifest, errors);

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
        if (
            value is null
            || (required && string.IsNullOrWhiteSpace(value))
            || value.Length > PluginContractLimits.MaximumDescriptionCharacters
            || value.Any(char.IsControl)
        )
        {
            errors.Add(new(PluginManifestErrorCode.InvalidText, location));
        }
    }

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
