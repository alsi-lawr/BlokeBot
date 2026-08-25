using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

internal static class PluginPageAssetEndpoints
{
    internal const string PageCsp =
        "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' https: data:; media-src 'self' https:; font-src 'self'; connect-src https:; frame-src https:; object-src 'none'; base-uri 'none'; form-action https:; frame-ancestors 'self'";

    internal static void MapPluginPageAssetEndpoints(this WebApplication app) =>
        _ = app.MapGet(
                "/plugins/{plugin}/hosts/{host:int}/features/{feature}/pages/{route}/assets/{**assetPath}",
                GetAssetAsync
            )
            .RequireAuthorization("HostSelected");

    private static async Task<IResult> GetAssetAsync(
        HttpContext http,
        string plugin,
        int host,
        string feature,
        string route,
        string assetPath,
        PluginPageAssetService assets,
        CancellationToken cancellationToken
    )
    {
        var session = AuthenticatedSession.FromPrincipal(http.User);
        var selectedHost = session.State.Match<int?>(
            static _ => null,
            static selected => selected.Selection.Current.Id,
            static _ => null
        );
        if (selectedHost != host)
        {
            return Results.Forbid();
        }
        if (
            !PluginId.TryCreate(plugin, out var pluginId)
            || !PluginHostId.TryCreate(host, out var hostId)
            || !PluginFeatureId.TryCreate(feature, out var featureId)
            || string.IsNullOrWhiteSpace(route)
            || string.IsNullOrWhiteSpace(assetPath)
        )
        {
            return Results.NotFound();
        }

        var resolution = await assets.ResolveAsync(
            pluginId,
            featureId,
            hostId,
            route,
            assetPath,
            cancellationToken
        );
        return resolution switch
        {
            PluginPageAssetResolution.Available available => Asset(http.Response, available.Asset),
            PluginPageAssetResolution.TooLarge => Results.StatusCode(
                StatusCodes.Status413PayloadTooLarge
            ),
            _ => Results.NotFound(),
        };
    }

    private static IResult Asset(HttpResponse response, PluginPageAsset asset)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        response.Headers.ContentSecurityPolicy = PageCsp;
        return Results.Bytes(asset.Content, asset.MediaType);
    }
}
