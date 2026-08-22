namespace BlokeBot.Plugins.Contracts;

public enum PluginTrustLevel
{
    FullyTrusted,
}

public enum PluginOperatingSystemAccess
{
    BlokeBotAccount,
}

public enum PluginProcessIsolationBoundary
{
    AvailabilityOnly,
}

public enum PluginStandardLibrary
{
    Full,
    Restricted,
}

public sealed record PluginTrustContract(
    PluginTrustLevel TrustLevel,
    PluginOperatingSystemAccess OperatingSystemAccess,
    PluginProcessIsolationBoundary ProcessIsolation,
    PluginStandardLibrary StandardLibrary
);

public sealed record PluginRuntimeContract(
    int ManifestVersion,
    int PackagePolicyVersion,
    int ValueContractVersion,
    PluginApiVersion HostApiVersion,
    PluginLuaVersion LuaVersion,
    PluginTrustContract Trust
)
{
    public static PluginRuntimeContract Current { get; } =
        new(
            ManifestVersion: 1,
            PackagePolicyVersion: 1,
            ValueContractVersion: 1,
            HostApiVersion: PluginApiVersion.V1,
            LuaVersion: PluginLuaVersion.Lua54,
            Trust: new(
                PluginTrustLevel.FullyTrusted,
                PluginOperatingSystemAccess.BlokeBotAccount,
                PluginProcessIsolationBoundary.AvailabilityOnly,
                PluginStandardLibrary.Full
            )
        );
}

public sealed record PluginEngineDescriptor(
    PluginEngineId Engine,
    PluginLuaVersion LuaVersion,
    PluginStandardLibrary StandardLibrary,
    bool SupportsCoroutines,
    bool SupportsCooperativeCancellation,
    int PackagePolicyVersion,
    int ValueContractVersion,
    PluginApiVersion HostApiVersion
);
