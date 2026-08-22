using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginHostCompatibilityTarget(
    SemanticVersion BlokeBotVersion,
    PluginApiVersion ApiVersion,
    ImmutableArray<PluginHostModuleDescriptor> HostModules
);

public enum PluginCompatibilityFailureCode
{
    UnsupportedManifestVersion,
    UnsupportedApiVersion,
    IncompatibleBlokeBotVersion,
    UnsupportedLuaVersion,
    MissingHostModule,
    IncompatibleHostModuleVersion,
    IncompatibleEngine,
}

public sealed record PluginCompatibilityFailure(
    PluginCompatibilityFailureCode Code,
    string Subject
);

public abstract record PluginCompatibilityOutcome
{
    private PluginCompatibilityOutcome() { }

    public sealed record Compatible : PluginCompatibilityOutcome;

    public sealed record Incompatible(IReadOnlyList<PluginCompatibilityFailure> Failures)
        : PluginCompatibilityOutcome;
}

public abstract record PluginEngineAdmissionOutcome
{
    private PluginEngineAdmissionOutcome() { }

    public sealed record Accepted(PluginEngineDescriptor Engine, PluginTrustContract Trust)
        : PluginEngineAdmissionOutcome;

    public sealed record Rejected(IReadOnlyList<PluginCompatibilityFailure> Failures)
        : PluginEngineAdmissionOutcome;
}
