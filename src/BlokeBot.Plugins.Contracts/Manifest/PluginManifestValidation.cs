namespace BlokeBot.Plugins.Contracts;

public enum PluginManifestErrorCode
{
    ManifestTooLarge,
    MalformedToml,
    InvalidText,
    InvalidMarketplace,
    InvalidCompatibilityRange,
    TooManyDeclarations,
    DuplicateIdentifier,
    DuplicatePath,
    CaseCollidingPath,
    InvalidPath,
    InvalidLuaModule,
    InvalidAsset,
    InvalidPayload,
    IncompatiblePayloadTarget,
    InvalidSetting,
    InvalidFeature,
    InvalidTwitchDeclaration,
    InvalidDispatchDeclaration,
    InvalidHostModule,
    InvalidMigration,
    InvalidAutomationDefinition,
    InvalidAutomationTemplate,
    InvalidPage,
    InvalidPluginValue,
    IncompatibleDeclaration,
}

public sealed record PluginManifestError(PluginManifestErrorCode Code, string Location);

public sealed class ValidatedPluginManifest
{
    internal ValidatedPluginManifest(PluginManifest manifest) => Manifest = manifest;

    public PluginManifest Manifest { get; }
}

internal sealed class ValidatedPluginManifestDeclaration
{
    internal ValidatedPluginManifestDeclaration(PluginManifest manifest) => Manifest = manifest;

    internal PluginManifest Manifest { get; }
}

internal abstract record PluginManifestDeclarationValidationOutcome
{
    private PluginManifestDeclarationValidationOutcome() { }

    internal sealed record Accepted(ValidatedPluginManifestDeclaration Declaration)
        : PluginManifestDeclarationValidationOutcome;

    internal sealed record Rejected(IReadOnlyList<PluginManifestError> Errors)
        : PluginManifestDeclarationValidationOutcome;
}

public abstract record PluginManifestValidationOutcome
{
    private PluginManifestValidationOutcome() { }

    public sealed record Accepted(ValidatedPluginManifest Manifest)
        : PluginManifestValidationOutcome;

    public sealed record Rejected(IReadOnlyList<PluginManifestError> Errors)
        : PluginManifestValidationOutcome;
}
