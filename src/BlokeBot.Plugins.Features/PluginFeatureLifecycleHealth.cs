using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public interface IPluginFeatureLifecycleHealth
{
    bool IsCurrent(PluginFeatureDeclaration declaration);

    bool IsHealthy(PluginFeatureDeclaration declaration);
}

public sealed class RuntimePluginFeatureLifecycleHealth(IPluginRuntimeSnapshotProvider runtime)
    : IPluginFeatureLifecycleHealth
{
    public bool IsCurrent(PluginFeatureDeclaration declaration) =>
        TryGetCurrent(declaration, out var entry)
        && entry.Phase is not (PluginLifecyclePhase.Removing or PluginLifecyclePhase.Removed);

    public bool IsHealthy(PluginFeatureDeclaration declaration) =>
        TryGetCurrent(declaration, out var entry)
        && entry.Phase == PluginLifecyclePhase.Active
        && entry.WorkerMode == PluginWorkerMode.Admitted;

    private bool TryGetCurrent(
        PluginFeatureDeclaration declaration,
        out PluginRuntimeEntry entry
    ) =>
        runtime.Current.Entries.TryGetValue(declaration.Installation.PluginId, out entry!)
        && entry.Installation == declaration.Installation
        && entry.Fence == declaration.Fence;
}
