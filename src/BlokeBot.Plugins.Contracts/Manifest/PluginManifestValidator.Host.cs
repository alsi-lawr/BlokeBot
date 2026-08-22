namespace BlokeBot.Plugins.Contracts;

public static partial class PluginManifestValidator
{
    private static void ValidateHostModulesAndMigrations(
        PluginManifest manifest,
        List<PluginManifestError> errors
    )
    {
        ValidateCount(manifest.HostModules, "$.hostModules", errors);
        ValidateCount(manifest.Migrations, "$.migrations", errors);
        ValidateDistinct(manifest.HostModules.Select(module => module.Id), "$.hostModules", errors);
        ValidateDistinct(
            manifest.Migrations.Select(migration => migration.Id),
            "$.migrations",
            errors
        );

        foreach (var module in manifest.HostModules)
        {
            if (module.MinimumVersion.CompareTo(module.MaximumVersion) > 0)
            {
                errors.Add(new(PluginManifestErrorCode.InvalidHostModule, "$.hostModules"));
            }
        }

        var moduleIds = manifest.LuaModules.Select(module => module.Id).ToHashSet();
        var transitions = new HashSet<(SemanticVersion From, SemanticVersion To)>();
        foreach (var migration in manifest.Migrations)
        {
            if (
                migration.FromVersion.CompareTo(migration.ToVersion) >= 0
                || migration.ToVersion.CompareTo(manifest.Release.DeclaredVersion) > 0
                || !moduleIds.Contains(migration.Module)
                || !ValidEntryPoint(migration.EntryPoint)
                || !transitions.Add((migration.FromVersion, migration.ToVersion))
            )
            {
                errors.Add(new(PluginManifestErrorCode.InvalidMigration, "$.migrations"));
            }
        }
    }
}
