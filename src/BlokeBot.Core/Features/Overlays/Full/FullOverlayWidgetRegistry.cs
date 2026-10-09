using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayWidgetRegistry(
    OverlayStateProvider sources,
    OverlayEventFeedService events,
    OverlayCueService media,
    OverlayRemoteUrlPolicy urls,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PluginWidgetCatalog plugins,
    IPluginWidgetInvoker invoker,
    PluginWidgetAssetService pluginAssets,
    ILogger<FullOverlayWidgetRegistry> logger
)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly ImmutableDictionary<FullOverlayWidgetKind, Registration> _builtins =
        Builtins();

    internal ImmutableArray<FullOverlayWidgetDescriptor> Descriptors =>
        [
            .. _builtins.Values.Select(registration => registration.Descriptor),
            .. plugins.Declarations.Select(widget => new FullOverlayWidgetDescriptor(
                PluginKind(widget.Plugin, widget.Descriptor.Id),
                widget.Descriptor.Title,
                typeof(PluginValue.Map),
                widget.Descriptor.ConfigurationFields
            )),
        ];

    internal FullOverlayWidget? Create(FullOverlayWidgetKind kind, Guid documentId) =>
        _builtins.TryGetValue(kind, out var builtin) ? Widget(kind, builtin.Default(documentId))
        : FindPlugin(kind) is { } plugin
            ? Widget(kind, FullOverlayPluginValues.ToJson(plugin.Descriptor.DefaultConfiguration))
        : null;

    internal async Task<ImmutableArray<FullOverlayWidgetProjection>> ProjectAsync(
        FullOverlayRenderContext context,
        CancellationToken ct
    )
    {
        var projections = ImmutableArray.CreateBuilder<FullOverlayWidgetProjection>();
        foreach (var widget in context.Document.Widgets)
        {
            FullOverlayWidgetOutput output;
            try
            {
                output =
                    widget.RequiresSetup ? Unavailable(widget, "destination-setup-required")
                    : _builtins.TryGetValue(widget.Kind, out var registration)
                        ? await registration.Project(this, context, widget, ct)
                    : await ProjectPluginAsync(context, widget, ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Full overlay widget {WidgetId} projection failed with {FailureClassification} ({FailureType}).",
                    widget.Id,
                    "source-unavailable",
                    exception.GetType().FullName
                );
                output = Unavailable(widget, "source-unavailable");
            }
            projections.Add(new(widget.Id, widget.Audio, output));
        }
        return projections.ToImmutable();
    }

    internal async ValueTask<PluginAssetContentResolution> ResolvePluginAssetAsync(
        FullOverlayRenderContext context,
        FullOverlayWidgetId widgetId,
        string assetPath,
        CancellationToken ct
    )
    {
        var widget = context.Document.Widgets.FirstOrDefault(widget => widget.Id == widgetId);
        return
            widget is null
            || FindPlugin(widget.Kind) is not { } declaration
            || !PluginHostId.TryCreate(context.HostId, out var host)
            || plugins.Resolve(declaration.Plugin, declaration.Descriptor.Id, host)
                is not { } endpoint
            ? new PluginAssetContentResolution.NotFound()
            : await ResolvePluginAssetAsync(context, widgetId, endpoint, assetPath, ct);
    }

    internal ValueTask<PluginAssetContentResolution> ResolvePluginAssetAsync(
        FullOverlayRenderContext context,
        FullOverlayWidgetId widgetId,
        PluginWidgetEndpoint endpoint,
        string assetPath,
        CancellationToken ct
    ) =>
        endpoint.State.Key.HostId.Value != context.HostId
        || !context.Document.Widgets.Any(widget =>
            widget.Id == widgetId
            && widget.Kind == PluginKind(endpoint.State.Key.PluginId, endpoint.Descriptor.Id)
        )
            ? ValueTask.FromResult<PluginAssetContentResolution>(
                new PluginAssetContentResolution.NotFound()
            )
            : pluginAssets.ResolveAsync(
                endpoint,
                assetPath,
                () => context.Version is null || IsSelected(context),
                ct
            );

    private bool IsSelected(FullOverlayRenderContext context)
    {
        if (context.Version is not { } version || context.SelectionRevision is not { } revision)
        {
            return false;
        }
        using var db = dbFactory.CreateDbContext();
        var selected = (
            from overlay in db.FullOverlays.AsNoTracking()
            join host in db.Hosts.AsNoTracking() on overlay.HostId equals host.Id
            join publication in db.FullOverlayPublications.AsNoTracking()
                on new { OverlayId = overlay.Id, Version = overlay.PublishedVersion } equals new
                {
                    publication.OverlayId,
                    Version = (long?)publication.Version,
                }
            where
                overlay.HostId == context.HostId
                && overlay.PublicId == context.OverlayId
                && !overlay.IsArchived
                && overlay.PublishedVersion == version.Value
                && overlay.Revision == revision.Value
                && (host.EnabledFeatures & HostFeatureFlags.Overlays) != 0
            select publication.DocumentJson
        ).SingleOrDefault();
        return selected is not null
            && FullOverlayDocuments.Serialize(
                FullOverlayDocuments.Deserialize(selected) with
                {
                    Diagnostics = [],
                }
            ) == FullOverlayDocuments.Serialize(context.Document with { Diagnostics = [] });
    }

    private async Task<FullOverlayWidgetOutput> ProjectPluginAsync(
        FullOverlayRenderContext context,
        FullOverlayWidget widget,
        CancellationToken ct
    )
    {
        if (
            FindPlugin(widget.Kind) is not { } declaration
            || !PluginHostId.TryCreate(context.HostId, out var host)
            || plugins.Resolve(declaration.Plugin, declaration.Descriptor.Id, host)
                is not { } endpoint
        )
        {
            return Unavailable(widget, "source-unavailable");
        }
        if (context.Version is not null && !IsSelected(context))
        {
            return Unavailable(widget, "source-unavailable");
        }
        if (
            FullOverlayPluginValues.FromJson(widget.Configuration) is not PluginValue.Map config
            || !PluginWidgetConfiguration.IsValid(endpoint.Descriptor, config)
        )
        {
            return Unavailable(widget, "invalid-configuration");
        }
        var outcome = await invoker.InvokeWidgetAsync(
            endpoint,
            new(
                endpoint.Declaration.Installation,
                host,
                endpoint.Descriptor.Id,
                context.OverlayId,
                context.Document.Id,
                widget.Id.Value,
                context.Version is not null ? PluginWidgetProjectionMode.Live
                    : context.DataMode == FullOverlayDataMode.Sample
                        ? PluginWidgetProjectionMode.PreviewSample
                    : PluginWidgetProjectionMode.PreviewLive
            ),
            config,
            ct
        );
        return
            outcome is PluginDispatchInvocationOutcome.Returned { Value: PluginValue.Map state }
            && plugins.IsCurrent(endpoint)
            && (context.Version is null || IsSelected(context))
            ? new FullOverlayWidgetOutput.Plugin(endpoint, state)
            : Unavailable(widget, "source-unavailable");
    }

    private (PluginId Plugin, PluginWidgetDescriptor Descriptor)? FindPlugin(
        FullOverlayWidgetKind kind
    )
    {
        foreach (var widget in plugins.Declarations)
        {
            if (PluginKind(widget.Plugin, widget.Descriptor.Id) == kind)
            {
                return widget;
            }
        }
        return null;
    }

    private static FullOverlayWidgetKind PluginKind(PluginId plugin, PluginWidgetId widget) =>
        new($"plugin:{plugin.Value}/{widget.Value}");

    private static FullOverlayWidget Widget(FullOverlayWidgetKind kind, JsonElement config) =>
        new(
            new(Guid.NewGuid()),
            kind,
            config,
            FullOverlayAuthoringMetadata.Default,
            FullOverlayAudio.Default
        );

    internal static FullOverlayWidgetOutput.Unavailable Unavailable(
        FullOverlayWidget widget,
        string code
    ) =>
        new(
            new(
                code,
                "This widget is unavailable. Check its source or configuration in the editor.",
                FullOverlayDiagnosticSeverity.Warning,
                widget.Id
            )
        );

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, _json);

    private static JsonElement SourceJson(OverlayConfiguration config)
    {
        using var document = JsonDocument.Parse(config.ToPersistenceJson());
        return document.RootElement.Clone();
    }

    private sealed record Registration(
        FullOverlayWidgetDescriptor Descriptor,
        Func<Guid, JsonElement> Default,
        Func<
            FullOverlayWidgetRegistry,
            FullOverlayRenderContext,
            FullOverlayWidget,
            CancellationToken,
            Task<FullOverlayWidgetOutput>
        > Project
    );
}
