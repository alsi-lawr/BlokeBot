namespace BlokeBot.Core.Features.Overlays.Full;

internal static partial class FullOverlayWebEndpoints
{
    private static void MapResources(WebApplication app)
    {
        var routes = app.MapGroup("/full-overlay/resources/{resourceId:guid}").AllowAnonymous();
        _ = routes.MapGet(
            "/plugin/{widgetId:guid}/{assetId:guid}/{**path}",
            async (
                Guid resourceId,
                Guid widgetId,
                Guid assetId,
                string path,
                HttpContext context,
                FullOverlayDelivery delivery,
                CancellationToken ct
            ) =>
            {
                ResourceHeaders(context);
                if (!HttpMethods.IsGet(context.Request.Method))
                {
                    return Results.StatusCode(405);
                }
                var asset = await delivery.PluginAssetAsync(
                    resourceId,
                    widgetId,
                    assetId,
                    path,
                    ct
                );
                return asset.Match<IResult>(
                    available =>
                        Results.Bytes(available.Asset.Content.ToArray(), available.Asset.MediaType),
                    _ => Results.NotFound(),
                    _ => Results.StatusCode(StatusCodes.Status413PayloadTooLarge)
                );
            }
        );
        _ = routes.MapGet(
            "/media/{assetId:guid}/{revision:int}",
            async (
                Guid resourceId,
                Guid assetId,
                int revision,
                HttpContext context,
                FullOverlayDelivery delivery,
                CancellationToken ct
            ) =>
            {
                ResourceHeaders(context);
                if (!HttpMethods.IsGet(context.Request.Method))
                {
                    return Results.StatusCode(405);
                }
                var asset = await delivery.MediaAsync(resourceId, assetId, revision, ct);
                return asset is null
                    ? Results.NotFound()
                    : Results.File(asset.Path, asset.ContentType, enableRangeProcessing: true);
            }
        );
    }

    private static void ResourceHeaders(HttpContext context)
    {
        Headers(context, _framePolicy);
        var response = context.Response;
        response.Headers.AccessControlAllowOrigin = "*";
        response.Headers.AccessControlAllowMethods = "GET";
    }
}
