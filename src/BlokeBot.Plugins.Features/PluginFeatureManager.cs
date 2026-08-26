using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed partial class PluginFeatureManager(
    IPluginFeatureStore store,
    IPluginFeatureDeclarationProvider declarations,
    IPluginFeatureLifecycleHealth lifecycleHealth,
    IPluginCoreDependencyChecker dependencies,
    IPluginFeatureReconciler reconciler,
    IPluginSecretProtector secrets,
    PluginSettingsValidator validator,
    PluginSettingValuesCodec codec,
    PluginFeatureSnapshotRegistry snapshots,
    IPluginLifecycleSerialization lifecycleSerialization,
    IPluginCommandActivationGate? commandActivation = null,
    IPluginFeatureWorkCoordinator? work = null,
    IPluginFeatureAutomationPlanner? automations = null
)
{
    public ValueTask<PluginFeatureState?> LoadFeatureStateAsync(
        PluginFeatureKey key,
        CancellationToken cancellationToken
    ) => store.LoadFeatureStateAsync(key, cancellationToken);

    private PluginFeatureDeclaration? Declaration(PluginConfigurationOwner owner) =>
        owner.Match(
            installation => FindDeclaration(installation.PluginId),
            feature => FindDeclaration(feature.Key.PluginId)
        );

    private PluginFeatureDeclaration? FindDeclaration(PluginId pluginId) =>
        declarations.Current.Declarations.TryGetValue(pluginId, out var declaration)
            ? declaration
            : null;

    private static PluginId PluginIdFor(PluginConfigurationOwner owner) =>
        owner.Match(
            static installation => installation.PluginId,
            static feature => feature.Key.PluginId
        );

    private static PluginFeatureDeclaration RelevantDeclaration(
        PluginFeatureDeclaration declaration,
        PluginFeatureDescriptor feature
    )
    {
        var settingIds = feature.Settings.ToHashSet();
        return declaration with
        {
            Manifest = declaration.Manifest with
            {
                Settings = declaration
                    .Manifest.Settings.Where(setting => settingIds.Contains(setting.Id))
                    .ToImmutableArray(),
            },
        };
    }

    private static PluginFeatureRevision NextRevision(PluginFeatureRevision? current) =>
        PluginFeatureRevision.TryCreate((current?.Value ?? 0) + 1, out var next)
            ? next
            : throw new InvalidOperationException("Plugin feature revision exhausted.");

    private static PluginReadinessReason PendingReason() =>
        PluginReadinessReason.TryCreate(
            PluginReadinessReasonCode.ReconciliationPending,
            PluginRecoveryAction.Retry,
            "Twitch setup is still in progress.",
            out var reason
        )
            ? reason
            : throw new InvalidOperationException("Invalid built-in readiness reason.");
}
