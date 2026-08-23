using System.Runtime.InteropServices;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public sealed record PluginWorkerExecutable(string Path)
{
    public bool IsManagedAssembly =>
        string.Equals(
            System.IO.Path.GetExtension(Path),
            ".dll",
            StringComparison.OrdinalIgnoreCase
        );
}

public abstract record PluginWorkerDiscoveryOutcome
{
    private PluginWorkerDiscoveryOutcome() { }

    public sealed record Found(PluginWorkerExecutable Executable) : PluginWorkerDiscoveryOutcome;

    public sealed record NotFound(IReadOnlyList<string> SearchedPaths)
        : PluginWorkerDiscoveryOutcome;
}

public static class PluginWorkerDiscovery
{
    public const string WorkerPathEnvironmentVariable = "BLOKEBOT_PLUGIN_WORKER_PATH";

    public static PluginWorkerDiscoveryOutcome Discover(string? applicationBaseDirectory = null)
    {
        var explicitPath = Environment.GetEnvironmentVariable(WorkerPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var fullExplicitPath = Path.GetFullPath(explicitPath);
            return File.Exists(fullExplicitPath)
                ? new PluginWorkerDiscoveryOutcome.Found(new(fullExplicitPath))
                : new PluginWorkerDiscoveryOutcome.NotFound([fullExplicitPath]);
        }

        var baseDirectory = Path.GetFullPath(applicationBaseDirectory ?? AppContext.BaseDirectory);
        var executableName = OperatingSystem.IsWindows()
            ? "BlokeBot.PluginWorker.exe"
            : "BlokeBot.PluginWorker";
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "plugin-worker", executableName),
            Path.Combine(baseDirectory, "plugin-worker", "BlokeBot.PluginWorker.dll"),
            Path.Combine(baseDirectory, executableName),
            Path.Combine(baseDirectory, "BlokeBot.PluginWorker.dll"),
        };
        var found = candidates.FirstOrDefault(File.Exists);
        return found is null
            ? new PluginWorkerDiscoveryOutcome.NotFound(candidates)
            : new PluginWorkerDiscoveryOutcome.Found(new(found));
    }
}

public static class PluginRuntimeIdentifierResolver
{
    public static bool TryResolveCurrent(out PluginRuntimeIdentifier runtimeIdentifier)
    {
        var candidate = (
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
            RuntimeInformation.ProcessArchitecture
        ) switch
        {
            (false, false, Architecture.X64) => PluginRuntimeIdentifier.LinuxX64,
            (false, false, Architecture.Arm64) => PluginRuntimeIdentifier.LinuxArm64,
            (false, true, Architecture.Arm64) => PluginRuntimeIdentifier.MacOsArm64,
            (true, false, Architecture.X64) => PluginRuntimeIdentifier.WindowsX64,
            (true, false, Architecture.Arm64) => PluginRuntimeIdentifier.WindowsArm64,
            _ => (PluginRuntimeIdentifier?)null,
        };
        runtimeIdentifier = candidate.GetValueOrDefault();
        return candidate.HasValue;
    }
}
