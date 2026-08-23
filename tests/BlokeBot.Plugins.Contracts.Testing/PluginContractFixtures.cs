using System.Text;

namespace BlokeBot.Plugins.Contracts.Testing;

public static class PluginContractFixtures
{
    private const string _manifestResource =
        "BlokeBot.Plugins.Contracts.Testing.Fixtures.complete-valid-manifest.json";

    public static byte[] CompleteManifestJson()
    {
        using var stream =
            typeof(PluginContractFixtures).Assembly.GetManifestResourceStream(_manifestResource)
            ?? throw new InvalidOperationException(
                "The complete plugin manifest fixture is unavailable."
            );
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static PluginHostCompatibilityTarget CompatibleHost() =>
        new(
            SemanticVersion("0.13.0"),
            PluginApiVersion.V1,
            PluginRuntimeIdentifier.LinuxX64,
            [
                new(
                    HostModuleId("chat"),
                    PluginApiVersion.V1,
                    [
                        new(
                            HostOperationId("send-message"),
                            [
                                PluginInvocationContextKind.Channel,
                                PluginInvocationContextKind.Automation,
                            ],
                            [PluginValueKind.String],
                            PluginValueKind.Nil,
                            MaximumArgumentBytes: PluginContractLimits.MaximumPluginValuePayloadBytes,
                            MaximumResultBytes: PluginContractLimits.MaximumPluginValuePayloadBytes
                        ),
                    ]
                ),
            ]
        );

    public static IReadOnlyList<PluginPackageEntry> CompletePackage()
    {
        var lua = Encoding.UTF8.GetBytes("return {}\n");
        return Array.AsReadOnly<PluginPackageEntry>([
            new PluginPackageEntry.File(PluginPackage.ManifestPath, CompleteManifestJson()),
            new PluginPackageEntry.File("lua/main.lua", lua),
            new PluginPackageEntry.File(
                "lua/events.lua",
                Encoding.UTF8.GetBytes("return { answer = 42 }\n")
            ),
            new PluginPackageEntry.File("lua/pages.lua", lua),
            new PluginPackageEntry.File("lua/migrations.lua", lua),
            new PluginPackageEntry.File(
                "web/index.html",
                Encoding.UTF8.GetBytes("<!doctype html><main></main>")
            ),
            new PluginPackageEntry.File("web/app.js", Encoding.UTF8.GetBytes("export {};")),
            new PluginPackageEntry.File("media/icon.webp", new byte[] { 0x52, 0x49, 0x46, 0x46 }),
            new PluginPackageEntry.File(
                "payloads/linux-x64/libqueue.so",
                new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01 }
            ),
            new PluginPackageEntry.File(
                "payloads/managed/Queue.Helper.dll",
                new byte[] { 0x4D, 0x5A, 0x90, 0x00 }
            ),
            new PluginPackageEntry.File(
                "payloads/portable/queue.wasm",
                new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00 }
            ),
        ]);
    }

    public static byte[] ManifestReplacing(string oldValue, string newValue) =>
        Encoding.UTF8.GetBytes(
            Encoding
                .UTF8.GetString(CompleteManifestJson())
                .Replace(oldValue, newValue, StringComparison.Ordinal)
        );

    public static IReadOnlyList<PluginPackageEntry> PackageWith(PluginPackageEntry entry) =>
        CompletePackage().Append(entry).ToArray();

    public static PluginEngineDescriptor CompatibleEngine(string id = "fixture-engine") =>
        new(
            EngineId(id),
            PluginLuaVersion.Lua54,
            PluginStandardLibrary.Full,
            SupportsCoroutines: true,
            SupportsCooperativeCancellation: true,
            PluginRuntimeContract.Current.PackagePolicyVersion,
            PluginRuntimeContract.Current.ValueContractVersion,
            PluginApiVersion.V1
        );

    public static PluginId PluginId(string value) =>
        BlokeBot.Plugins.Contracts.PluginId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid plugin ID fixture '{value}'.");

    public static PluginHostModuleId HostModuleId(string value) =>
        PluginHostModuleId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid host module ID fixture '{value}'.");

    public static PluginHostOperationId HostOperationId(string value) =>
        PluginHostOperationId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid host operation ID fixture '{value}'.");

    public static PluginEngineId EngineId(string value) =>
        PluginEngineId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid engine ID fixture '{value}'.");

    public static PluginHostCallId HostCallId() =>
        PluginHostCallId.TryCreate(Guid.NewGuid(), out var id)
            ? id
            : throw new InvalidOperationException("Invalid host call ID fixture.");

    public static PluginCoroutineId CoroutineId() =>
        PluginCoroutineId.TryCreate(Guid.NewGuid(), out var id)
            ? id
            : throw new InvalidOperationException("Invalid coroutine ID fixture.");

    public static PluginAutomationInvocationId AutomationInvocationId() =>
        PluginAutomationInvocationId.TryCreate(Guid.NewGuid(), out var id)
            ? id
            : throw new InvalidOperationException("Invalid automation invocation ID fixture.");

    public static PluginPageSessionId PageSessionId() =>
        PluginPageSessionId.TryCreate(Guid.NewGuid(), out var id)
            ? id
            : throw new InvalidOperationException("Invalid page session ID fixture.");

    public static SemanticVersion SemanticVersion(string value) =>
        BlokeBot.Plugins.Contracts.SemanticVersion.TryCreate(value, out var version)
            ? version
            : throw new InvalidOperationException($"Invalid semantic version fixture '{value}'.");
}
