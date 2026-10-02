using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed record PluginWidgetEndpoint(
    PluginFeatureDeclaration Declaration,
    PluginWidgetDescriptor Descriptor,
    PluginFeatureState State,
    PluginHostOperationId Operation
);

public sealed class PluginWidgetCatalog(
    IPluginFeatureDeclarationProvider declarations,
    IPluginFeatureSnapshotProvider features,
    IPluginRuntimeSnapshotProvider runtime
)
{
    public IEnumerable<(PluginId Plugin, PluginWidgetDescriptor Descriptor)> Declarations =>
        declarations.Current.Declarations.Values.SelectMany(declaration =>
            declaration.Manifest.Widgets.Select(widget =>
                (declaration.Installation.PluginId, widget)
            )
        );

    public PluginWidgetEndpoint? Resolve(
        PluginId pluginId,
        PluginWidgetId widgetId,
        PluginHostId hostId
    ) =>
        !declarations.Current.Declarations.TryGetValue(pluginId, out var declaration)
        || declaration.Manifest.Widgets.FirstOrDefault(widget => widget.Id == widgetId)
            is not { } widget
        || !runtime.Current.Entries.TryGetValue(pluginId, out var entry)
        || entry.Installation != declaration.Installation
        || entry.Fence != declaration.Fence
        || entry.Phase != PluginLifecyclePhase.Active
        || entry.WorkerMode != PluginWorkerMode.Admitted
        || !features.Current.States.TryGetValue(
            new(pluginId, widget.FeatureId, hostId),
            out var state
        )
        || state.Fence != declaration.Fence
        || state.Readiness is not PluginFeatureReadiness.Ready
        || !PluginHostOperationId.TryCreate(widget.RenderEntryPoint, out var operation)
            ? null
            : new(declaration, widget, state, operation);

    public bool IsCurrent(PluginWidgetEndpoint endpoint) =>
        Resolve(endpoint.State.Key.PluginId, endpoint.Descriptor.Id, endpoint.State.Key.HostId)
            is { } current
        && current.Declaration.Installation == endpoint.Declaration.Installation
        && current.Declaration.Fence == endpoint.Declaration.Fence
        && current.Declaration.PackageOperationId == endpoint.Declaration.PackageOperationId
        && current.State.Generation == endpoint.State.Generation
        && current.State.Revision == endpoint.State.Revision
        && current.State.Readiness == endpoint.State.Readiness;
}
