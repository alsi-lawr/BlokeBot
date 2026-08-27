namespace BlokeBot.Plugins.Contracts;

public static class PluginCompatibilityEvaluator
{
    public static PluginCompatibilityOutcome Evaluate(
        PluginManifest manifest,
        PluginHostCompatibilityTarget target
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(target);
        var failures = new List<PluginCompatibilityFailure>();
        var runtime = PluginRuntimeContract.Current;

        if (manifest.ManifestVersion != runtime.ManifestVersion)
        {
            failures.Add(
                new(
                    PluginCompatibilityFailureCode.UnsupportedManifestVersion,
                    manifest.ManifestVersion.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                )
            );
        }

        var declaration = manifest.Compatibility;
        if (
            target.ApiVersion.CompareTo(declaration.MinimumApiVersion) < 0
            || target.ApiVersion.CompareTo(declaration.MaximumApiVersion) > 0
        )
        {
            failures.Add(
                new(
                    PluginCompatibilityFailureCode.UnsupportedApiVersion,
                    target.ApiVersion.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                )
            );
        }

        if (
            target.BlokeBotVersion.CompareTo(declaration.MinimumBlokeBotVersion) < 0
            || target.BlokeBotVersion.CompareTo(declaration.MaximumBlokeBotVersionExclusive) >= 0
        )
        {
            failures.Add(
                new(
                    PluginCompatibilityFailureCode.IncompatibleBlokeBotVersion,
                    target.BlokeBotVersion.Value
                )
            );
        }

        if (declaration.LuaVersion != runtime.LuaVersion)
        {
            failures.Add(
                new(
                    PluginCompatibilityFailureCode.UnsupportedLuaVersion,
                    declaration.LuaVersion.ToString()
                )
            );
        }

        if (!declaration.SupportedTargets.Contains(target.RuntimeIdentifier))
        {
            failures.Add(
                new(
                    PluginCompatibilityFailureCode.UnsupportedReleaseTarget,
                    target.RuntimeIdentifier.ToString()
                )
            );
        }

        EvaluateHostModules(manifest.HostModules, target.HostModules, failures);
        return failures.Count == 0
            ? new PluginCompatibilityOutcome.Compatible()
            : new PluginCompatibilityOutcome.Incompatible(failures.AsReadOnly());
    }

    public static PluginEngineAdmissionOutcome AdmitEngine(PluginEngineDescriptor engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var current = PluginRuntimeContract.Current;
        var compatible =
            engine.LuaVersion == current.LuaVersion
            && engine.StandardLibrary == PluginStandardLibrary.Full
            && engine.SupportsCoroutines
            && engine.SupportsCooperativeCancellation
            && engine.PackagePolicyVersion == current.PackagePolicyVersion
            && engine.ValueContractVersion == current.ValueContractVersion
            && engine.HostApiVersion == current.HostApiVersion;

        return compatible
            ? new PluginEngineAdmissionOutcome.Accepted(engine, current.Trust)
            : new PluginEngineAdmissionOutcome.Rejected([
                new(PluginCompatibilityFailureCode.IncompatibleEngine, engine.Engine.Value),
            ]);
    }

    private static void EvaluateHostModules(
        IReadOnlyList<PluginHostModuleRequirement> requirements,
        IReadOnlyList<PluginHostModuleDescriptor> availableModules,
        List<PluginCompatibilityFailure> failures
    )
    {
        foreach (var requirement in requirements)
        {
            var module = availableModules.FirstOrDefault(available =>
                available.Id == requirement.Id
            );
            if (module is null)
            {
                failures.Add(
                    new(PluginCompatibilityFailureCode.MissingHostModule, requirement.Id.Value)
                );
                continue;
            }

            if (
                module.Version.CompareTo(requirement.MinimumVersion) < 0
                || module.Version.CompareTo(requirement.MaximumVersion) > 0
            )
            {
                failures.Add(
                    new(
                        PluginCompatibilityFailureCode.IncompatibleHostModuleVersion,
                        requirement.Id.Value
                    )
                );
            }
        }
    }
}
