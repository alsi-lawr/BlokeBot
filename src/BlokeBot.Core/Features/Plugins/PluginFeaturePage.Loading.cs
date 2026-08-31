using BlokeBot.Core.Components.Layout;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public partial class PluginFeaturePage
{
    private async Task LoadCoreAsync(
        PluginId pluginId,
        PluginFeatureId featureId,
        PluginHostId hostId
    )
    {
        var resolution = _pages.Resolve(pluginId, featureId, hostId, RouteValue);
        var definition = Definition(resolution);
        if (definition is not null)
        {
            _pluginName = definition.Declaration.Manifest.Name;
            _title = definition.Title;
            _description = definition.Feature.Description;
        }
        if (resolution is not PluginPageResolution.Available available)
        {
            PresentUnavailable(resolution);
            _loadState = new PageLoadState.Ready();
            return;
        }

        _endpoint = available.Endpoint;
        var ownOrigin = new Uri(_navigation.BaseUri).GetLeftPart(UriPartial.Authority);
        var origins = available.Endpoint.Definition is PluginPageDefinition.Embedded embedded
            ? embedded
                .Descriptor.MessageOrigins.Select(static origin =>
                    origin.GetLeftPart(UriPartial.Authority)
                )
                .Prepend(ownOrigin)
            : [ownOrigin];
        if (
            _sessions.Create(available.Endpoint, origins)
            is not PluginPageSessionCreation.Created created
        )
        {
            _endpoint = null;
            PresentState(
                "This plugin page is busy",
                "Too many plugin pages are open. Close another plugin page and try again.",
                null,
                string.Empty
            );
            _loadState = new PageLoadState.Ready();
            return;
        }
        _session = created.Session;
        if (available.Endpoint.Definition is PluginPageDefinition.Generated)
        {
            await LoadGeneratedAsync(available.Endpoint);
        }
        else
        {
            LoadEmbedded((PluginPageDefinition.Embedded)available.Endpoint.Definition, hostId);
        }
        _loadState = new PageLoadState.Ready();
    }

    private async Task LoadGeneratedAsync(PluginPageEndpoint endpoint)
    {
        var context = new PluginInvocationContext.Page(
            endpoint.Definition.Declaration.Installation,
            endpoint.State.Key.HostId,
            endpoint.Definition.Id,
            _session!.Id
        );
        var input = PluginInvocationInputs.Page(endpoint.State.Key.HostId, _session.Id);
        var outcome = await _invoker.InvokePageAsync(
            endpoint,
            context,
            input,
            CancellationToken.None
        );
        if (
            outcome is PluginDispatchInvocationOutcome.Returned returned
            && PluginPageDocumentParser.Parse(returned.Value, endpoint.Definition.Feature)
                is PluginPageDocumentParseOutcome.Parsed parsed
        )
        {
            _document = parsed.Document;
            return;
        }

        RemoveSession();
        _endpoint = null;
        PresentState(
            "This plugin page could not be rendered",
            "The plugin returned an unavailable or invalid page. Check the plugin feature, then try again.",
            FeatureSettingsRoute(endpoint.Definition),
            "Open feature settings"
        );
    }

    private void LoadEmbedded(PluginPageDefinition.Embedded embedded, PluginHostId hostId)
    {
        var asset = embedded.Declaration.Manifest.Assets.Single(candidate =>
            candidate.Id == embedded.Descriptor.DocumentAsset
        );
        _embeddedUrl =
            $"/plugins/{Uri.EscapeDataString(embedded.Declaration.Installation.PluginId.Value)}/hosts/{hostId.Value}/features/{Uri.EscapeDataString(embedded.Feature.Id.Value)}/pages/{Uri.EscapeDataString(embedded.Route)}/assets/{EscapePath(asset.Path)}";
        _bridgePending = true;
    }

    private void PresentUnavailable(PluginPageResolution resolution)
    {
        var definition = Definition(resolution);
        var featureRoute = definition is null ? null : FeatureSettingsRoute(definition);
        switch (resolution)
        {
            case PluginPageResolution.Disabled:
                PresentState(
                    "This plugin feature is off",
                    "Enable the feature for the selected channel before opening this page.",
                    featureRoute,
                    "Open feature settings"
                );
                break;
            case PluginPageResolution.NeedsAttention attention:
                PresentState(
                    "This plugin feature needs attention",
                    attention.Detail,
                    featureRoute,
                    "Open feature settings"
                );
                break;
            case PluginPageResolution.Removed:
                PresentState(
                    "This plugin was removed",
                    "Reinstall the plugin to use this page. Removing it deleted its settings and data.",
                    null,
                    string.Empty
                );
                break;
            case PluginPageResolution.Faulted:
                PresentState(
                    "This plugin needs repair",
                    "Ask a bot admin to repair or restart the plugin installation before using this page.",
                    null,
                    string.Empty
                );
                break;
            case PluginPageResolution.Missing:
                PresentState(
                    "This plugin page is no longer available",
                    "The plugin or page declaration could not be found.",
                    null,
                    string.Empty
                );
                break;
            default:
                PresentState(
                    "This plugin page is temporarily unavailable",
                    "The plugin is starting, stopping, or updating. Try again when it is ready.",
                    null,
                    string.Empty
                );
                break;
        }
    }

    private void PresentState(
        string title,
        string description,
        string? actionRoute,
        string actionLabel
    )
    {
        _stateTitle = title;
        _stateDescription = description;
        _stateActionRoute = actionRoute;
        _stateActionLabel = actionLabel;
    }

    private static PluginPageDefinition? Definition(PluginPageResolution resolution) =>
        resolution switch
        {
            PluginPageResolution.Available available => available.Endpoint.Definition,
            PluginPageResolution.Disabled disabled => disabled.Definition,
            PluginPageResolution.NeedsAttention attention => attention.Definition,
            PluginPageResolution.Removed removed => removed.Definition,
            PluginPageResolution.Faulted faulted => faulted.Definition,
            PluginPageResolution.Unavailable unavailable => unavailable.Definition,
            _ => null,
        };

    private static string FeatureSettingsRoute(PluginPageDefinition definition) =>
        $"/plugins/{definition.Declaration.Installation.PluginId.Value}/features/{definition.Feature.Id.Value}";

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
