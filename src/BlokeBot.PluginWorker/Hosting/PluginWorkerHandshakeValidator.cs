using BlokeBot.Plugins.Contracts;

namespace BlokeBot.PluginWorker;

internal static class PluginWorkerHandshakeValidator
{
    internal static PluginWorkerHandshakeFailure? Validate(
        PluginWorkerMessage.HostHandshake handshake,
        PluginRuntimeIdentifier runtimeIdentifier,
        PluginWorkerLaunchArguments arguments
    )
    {
        var compatibilityFailure = PluginWorkerCompatibility.Compare(handshake.Compatibility);
        if (compatibilityFailure is not null)
        {
            return compatibilityFailure;
        }

        if (
            handshake.Engine != KeraLuaPluginEngine.Descriptor
            || PluginCompatibilityEvaluator.AdmitEngine(KeraLuaPluginEngine.Descriptor)
                is PluginEngineAdmissionOutcome.Rejected
        )
        {
            return new(PluginWorkerHandshakeFailureCode.EngineMismatch, "KeraLua 1.4.9");
        }

        if (handshake.Package.RuntimeIdentifier != runtimeIdentifier)
        {
            return new(
                PluginWorkerHandshakeFailureCode.TargetMismatch,
                handshake.Package.RuntimeIdentifier.ToString()
            );
        }

        var packageMatches =
            !handshake.Package.LuaModules.IsDefaultOrEmpty
            && handshake.Package.LuaModules.Any(module =>
                module.Id == handshake.Package.EntryModule
            )
            && handshake.Package.LuaModules.All(module =>
                ModuleExists(arguments.PackageRoot, module.Path)
            );
        return !packageMatches
                ? new(
                    PluginWorkerHandshakeFailureCode.PackageMismatch,
                    handshake.Package.Plugin.PluginId.Value
                )
            : !Directory.Exists(arguments.PackageRoot)
                ? new(
                    PluginWorkerHandshakeFailureCode.PackageUnavailable,
                    handshake.Package.Plugin.PluginId.Value
                )
            : PrepareWritableState(arguments.StateRoot);
    }

    private static bool ModuleExists(string packageRoot, string canonicalPath)
    {
        if (
            string.IsNullOrWhiteSpace(canonicalPath)
            || Path.IsPathRooted(canonicalPath)
            || canonicalPath.Contains('\\', StringComparison.Ordinal)
        )
        {
            return false;
        }

        var root = Path.GetFullPath(packageRoot);
        var path = Path.GetFullPath(
            Path.Combine(root, canonicalPath.Replace('/', Path.DirectorySeparatorChar))
        );
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : $"{root}{Path.DirectorySeparatorChar}";
        return path.StartsWith(prefix, StringComparison.Ordinal) && File.Exists(path);
    }

    private static PluginWorkerHandshakeFailure? PrepareWritableState(string stateRoot)
    {
        try
        {
            _ = Directory.CreateDirectory(stateRoot);
            var probe = Path.Combine(stateRoot, $".worker-write-probe-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            Directory.SetCurrentDirectory(stateRoot);
            return null;
        }
        catch (IOException)
        {
            return new(PluginWorkerHandshakeFailureCode.PackageUnavailable, "Worker state path");
        }
        catch (UnauthorizedAccessException)
        {
            return new(PluginWorkerHandshakeFailureCode.PackageUnavailable, "Worker state path");
        }
    }
}
