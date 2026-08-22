using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public enum PluginRuntimeIdentifier
{
    [JsonStringEnumMemberName("linux-x64")]
    LinuxX64,

    [JsonStringEnumMemberName("linux-arm64")]
    LinuxArm64,

    [JsonStringEnumMemberName("osx-arm64")]
    MacOsArm64,

    [JsonStringEnumMemberName("win-x64")]
    WindowsX64,

    [JsonStringEnumMemberName("win-arm64")]
    WindowsArm64,
}

public sealed record PluginHostCompatibilityTarget(
    SemanticVersion BlokeBotVersion,
    PluginApiVersion ApiVersion,
    PluginRuntimeIdentifier RuntimeIdentifier,
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
    IncompatiblePayloadTarget,
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
