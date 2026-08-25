using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

internal sealed record PluginFeatureNavigationItem(
    PluginFeatureId Id,
    string Name,
    PluginFeatureReadiness Readiness,
    ImmutableArray<PluginPageNavigationItem> Pages
);

internal sealed record PluginPageNavigationItem(string Route, string Title);

internal sealed record PluginNavigationItem(
    PluginId Id,
    string Name,
    ImmutableArray<PluginFeatureNavigationItem> Features
);

internal static class PluginFeatureNavigation
{
    public static ImmutableArray<PluginNavigationItem> Project(
        PluginHostId? hostId,
        PluginFeatureDeclarationSnapshot declarations,
        PluginFeatureSnapshot snapshot
    ) =>
        declarations
            .Declarations.Values.OrderBy(static declaration => declaration.Manifest.Name)
            .ThenBy(static declaration => declaration.Manifest.Id.Value)
            .Select(declaration => new PluginNavigationItem(
                declaration.Manifest.Id,
                declaration.Manifest.Name,
                hostId is null
                    ? []
                    : declaration
                        .Manifest.Features.OrderBy(static feature => feature.Name)
                        .ThenBy(static feature => feature.Id.Value)
                        .Select(feature =>
                        {
                            var key = new PluginFeatureKey(
                                declaration.Manifest.Id,
                                feature.Id,
                                hostId
                            );
                            var readiness = snapshot.States.TryGetValue(key, out var state)
                                ? state.Readiness
                                : new PluginFeatureReadiness.Disabled();
                            return new PluginFeatureNavigationItem(
                                feature.Id,
                                feature.Name,
                                readiness,
                                declaration
                                    .Manifest.GeneratedPages.Where(page =>
                                        page.FeatureId == feature.Id
                                    )
                                    .Select(static page => new PluginPageNavigationItem(
                                        page.Route,
                                        page.Title
                                    ))
                                    .Concat(
                                        declaration
                                            .Manifest.EmbeddedPages.Where(page =>
                                                page.FeatureId == feature.Id
                                            )
                                            .Select(static page => new PluginPageNavigationItem(
                                                page.Route,
                                                page.Title
                                            ))
                                    )
                                    .OrderBy(static page => page.Title)
                                    .ThenBy(static page => page.Route)
                                    .ToImmutableArray()
                            );
                        })
                        .ToImmutableArray()
            ))
            .ToImmutableArray();
}
