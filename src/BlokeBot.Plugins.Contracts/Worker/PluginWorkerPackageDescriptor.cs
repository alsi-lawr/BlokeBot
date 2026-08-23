using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public enum PluginWorkerMode
{
    Admitted,
    Staging,
}

public sealed record PluginWorkerLuaModule(PluginLuaModuleId Id, string Path);

public sealed record PluginWorkerPackageDescriptor(
    PluginInstallationIdentity Plugin,
    PluginRuntimeIdentifier RuntimeIdentifier,
    PluginLuaModuleId EntryModule,
    ImmutableArray<PluginWorkerLuaModule> LuaModules
);

public sealed record PluginWorkerCompatibilityDescriptor(
    int ProtocolVersion,
    int ManifestVersion,
    int PackagePolicyVersion,
    int ValueContractVersion,
    PluginApiVersion HostApiVersion,
    PluginLuaVersion LuaVersion,
    PluginStandardLibrary StandardLibrary
)
{
    public static PluginWorkerCompatibilityDescriptor Current { get; } =
        new(
            PluginWorkerLimits.ProtocolVersion,
            PluginRuntimeContract.Current.ManifestVersion,
            PluginRuntimeContract.Current.PackagePolicyVersion,
            PluginRuntimeContract.Current.ValueContractVersion,
            PluginRuntimeContract.Current.HostApiVersion,
            PluginRuntimeContract.Current.LuaVersion,
            PluginRuntimeContract.Current.Trust.StandardLibrary
        );
}

public static class PluginWorkerEngineContract
{
    public static PluginEngineDescriptor Selected { get; } =
        new(
            EngineId(),
            PluginLuaVersion.Lua54,
            PluginStandardLibrary.Full,
            SupportsCoroutines: true,
            SupportsCooperativeCancellation: true,
            PluginRuntimeContract.Current.PackagePolicyVersion,
            PluginRuntimeContract.Current.ValueContractVersion,
            PluginRuntimeContract.Current.HostApiVersion
        );

    private static PluginEngineId EngineId() =>
        PluginEngineId.TryCreate("keralua-1.4.9-lua-5.4.8", out var id)
            ? id
            : throw new InvalidOperationException("Invalid KeraLua engine identifier.");
}

public static class PluginWorkerPackageCompatibility
{
    public static bool Matches(
        PluginWorkerPackageDescriptor left,
        PluginWorkerPackageDescriptor right
    ) =>
        left.Plugin == right.Plugin
        && left.RuntimeIdentifier == right.RuntimeIdentifier
        && left.EntryModule == right.EntryModule
        && left.LuaModules.SequenceEqual(right.LuaModules);
}

public static class PluginWorkerCompatibility
{
    public static PluginWorkerHandshakeFailure? Compare(
        PluginWorkerCompatibilityDescriptor actual
    ) =>
        actual.ProtocolVersion != PluginWorkerCompatibilityDescriptor.Current.ProtocolVersion
            ? new(PluginWorkerHandshakeFailureCode.ProtocolSkew, "IPC protocol")
        : actual.HostApiVersion != PluginWorkerCompatibilityDescriptor.Current.HostApiVersion
            ? new(PluginWorkerHandshakeFailureCode.ApiMismatch, "Plugin host API")
        : actual.LuaVersion != PluginWorkerCompatibilityDescriptor.Current.LuaVersion
        || actual.StandardLibrary != PluginWorkerCompatibilityDescriptor.Current.StandardLibrary
            ? new(PluginWorkerHandshakeFailureCode.EngineMismatch, "Lua engine contract")
        : actual.ManifestVersion != PluginWorkerCompatibilityDescriptor.Current.ManifestVersion
        || actual.PackagePolicyVersion
            != PluginWorkerCompatibilityDescriptor.Current.PackagePolicyVersion
        || actual.ValueContractVersion
            != PluginWorkerCompatibilityDescriptor.Current.ValueContractVersion
            ? new(PluginWorkerHandshakeFailureCode.PackageMismatch, "Plugin package contract")
        : null;
}
