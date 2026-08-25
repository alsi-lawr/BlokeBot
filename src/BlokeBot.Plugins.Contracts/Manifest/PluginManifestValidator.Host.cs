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
        var transitions = new HashSet<PluginMigrationTransition>(
            PluginMigrationTransitionPrecedenceComparer.Instance
        );
        foreach (var migration in manifest.Migrations)
        {
            if (
                migration.FromVersion.CompareTo(migration.ToVersion) >= 0
                || migration.ToVersion.CompareTo(manifest.Release.DeclaredVersion) > 0
                || !moduleIds.Contains(migration.Module)
                || !PluginHostOperationId.TryCreate(migration.EntryPoint, out _)
                || !transitions.Add(new(migration.FromVersion, migration.ToVersion))
            )
            {
                errors.Add(new(PluginManifestErrorCode.InvalidMigration, "$.migrations"));
            }
        }
    }

    private sealed record PluginMigrationTransition(SemanticVersion From, SemanticVersion To);

    private sealed class PluginMigrationTransitionPrecedenceComparer
        : IEqualityComparer<PluginMigrationTransition>
    {
        internal static PluginMigrationTransitionPrecedenceComparer Instance { get; } = new();

        public bool Equals(PluginMigrationTransition? left, PluginMigrationTransition? right) =>
            ReferenceEquals(left, right)
            || (
                left is not null
                && right is not null
                && left.From.HasSamePrecedenceAs(right.From)
                && left.To.HasSamePrecedenceAs(right.To)
            );

        public int GetHashCode(PluginMigrationTransition transition) =>
            HashCode.Combine(
                transition.From.GetPrecedenceHashCode(),
                transition.To.GetPrecedenceHashCode()
            );
    }
}
