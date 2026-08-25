using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public abstract record PluginPageDefinition
{
    private protected PluginPageDefinition(
        PluginFeatureDeclaration declaration,
        PluginFeatureDescriptor feature,
        PluginPageId id,
        string route,
        string title
    )
    {
        Declaration = declaration;
        Feature = feature;
        Id = id;
        Route = route;
        Title = title;
    }

    public PluginFeatureDeclaration Declaration { get; }

    public PluginFeatureDescriptor Feature { get; }

    public PluginPageId Id { get; }

    public string Route { get; }

    public string Title { get; }

    public sealed record Generated : PluginPageDefinition
    {
        internal Generated(
            PluginFeatureDeclaration declaration,
            PluginFeatureDescriptor feature,
            PluginGeneratedPageDescriptor descriptor,
            PluginHostOperationId operation
        )
            : base(declaration, feature, descriptor.Id, descriptor.Route, descriptor.Title)
        {
            Descriptor = descriptor;
            Operation = operation;
        }

        public PluginGeneratedPageDescriptor Descriptor { get; }

        public PluginHostOperationId Operation { get; }
    }

    public sealed record Embedded : PluginPageDefinition
    {
        internal Embedded(
            PluginFeatureDeclaration declaration,
            PluginFeatureDescriptor feature,
            PluginEmbeddedPageDescriptor descriptor
        )
            : base(declaration, feature, descriptor.Id, descriptor.Route, descriptor.Title) =>
            Descriptor = descriptor;

        public PluginEmbeddedPageDescriptor Descriptor { get; }
    }
}

public sealed record PluginPageEndpoint(PluginPageDefinition Definition, PluginFeatureState State);

public abstract record PluginPageResolution
{
    private PluginPageResolution() { }

    public sealed record Available(PluginPageEndpoint Endpoint) : PluginPageResolution;

    public sealed record Disabled(PluginPageDefinition Definition) : PluginPageResolution;

    public sealed record NeedsAttention(PluginPageDefinition Definition, string Detail)
        : PluginPageResolution;

    public sealed record Removed(PluginPageDefinition Definition) : PluginPageResolution;

    public sealed record Faulted(PluginPageDefinition Definition) : PluginPageResolution;

    public sealed record Unavailable(PluginPageDefinition Definition) : PluginPageResolution;

    public sealed record Missing : PluginPageResolution;
}

public sealed class PluginPageCatalog(
    IPluginFeatureDeclarationProvider declarations,
    IPluginFeatureSnapshotProvider features,
    IPluginRuntimeSnapshotProvider runtime
)
{
    public PluginPageResolution Resolve(
        PluginId pluginId,
        PluginFeatureId featureId,
        PluginHostId hostId,
        string route
    )
    {
        if (
            !declarations.Current.Declarations.TryGetValue(pluginId, out var declaration)
            || declaration.FindFeature(featureId) is not { } feature
            || FindDefinition(declaration, feature, route) is not { } definition
        )
        {
            return new PluginPageResolution.Missing();
        }

        if (!runtime.Current.Entries.TryGetValue(pluginId, out var runtimeEntry))
        {
            return new PluginPageResolution.Unavailable(definition);
        }
        if (runtimeEntry.Installation != declaration.Installation)
        {
            return new PluginPageResolution.Removed(definition);
        }
        if (runtimeEntry.Phase is PluginLifecyclePhase.Removed)
        {
            return new PluginPageResolution.Removed(definition);
        }
        if (runtimeEntry.Phase is PluginLifecyclePhase.Faulted)
        {
            return new PluginPageResolution.Faulted(definition);
        }
        if (
            runtimeEntry.Fence != declaration.Fence
            || runtimeEntry.Phase is not PluginLifecyclePhase.Active
            || runtimeEntry.WorkerMode is not PluginWorkerMode.Admitted
        )
        {
            return new PluginPageResolution.Unavailable(definition);
        }

        var key = new PluginFeatureKey(pluginId, featureId, hostId);
        return !features.Current.States.TryGetValue(key, out var state)
                ? new PluginPageResolution.Disabled(definition)
            : state.Fence != declaration.Fence ? new PluginPageResolution.Unavailable(definition)
            : state.Readiness switch
            {
                PluginFeatureReadiness.Disabled => new PluginPageResolution.Disabled(definition),
                PluginFeatureReadiness.EnabledDegraded degraded =>
                    new PluginPageResolution.NeedsAttention(definition, degraded.Reason.Detail),
                PluginFeatureReadiness.Ready => new PluginPageResolution.Available(
                    new(definition, state)
                ),
                _ => new PluginPageResolution.Unavailable(definition),
            };
    }

    private static PluginPageDefinition? FindDefinition(
        PluginFeatureDeclaration declaration,
        PluginFeatureDescriptor feature,
        string route
    )
    {
        var generated = declaration.Manifest.GeneratedPages.FirstOrDefault(page =>
            page.FeatureId == feature.Id
            && string.Equals(page.Route, route, StringComparison.OrdinalIgnoreCase)
        );
        if (
            generated is not null
            && PluginHostOperationId.TryCreate(generated.RenderEntryPoint, out var operation)
        )
        {
            return new PluginPageDefinition.Generated(declaration, feature, generated, operation);
        }

        var embedded = declaration.Manifest.EmbeddedPages.FirstOrDefault(page =>
            page.FeatureId == feature.Id
            && string.Equals(page.Route, route, StringComparison.OrdinalIgnoreCase)
        );
        return embedded is null
            ? null
            : new PluginPageDefinition.Embedded(declaration, feature, embedded);
    }
}
