using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Net.Http.Headers;

namespace BlokeBot.Core.Features.Overlays.Full;

internal static partial class FullOverlayWebEndpoints
{
    private const string _outerPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'";
    private const string _framePolicy =
        "sandbox allow-scripts; default-src * data: blob:; script-src * data: blob: 'unsafe-inline' 'unsafe-eval'; style-src * 'unsafe-inline'; img-src * data: blob:; media-src * data: blob:; connect-src *; frame-src * data: blob:; base-uri *; form-action 'none'";

    internal static void MapFullOverlayEndpoints(this WebApplication app)
    {
        _ = app.MapGet(
                "/full-overlay/assets/controller.js",
                (HttpContext context) =>
                    Asset(context, FullOverlayBrowserAssets.Controller, "text/javascript")
            )
            .AllowAnonymous();
        _ = app.MapGet(
                "/full-overlay/assets/frame.js",
                (HttpContext context) =>
                    Asset(context, FullOverlayBrowserAssets.Frame, "text/javascript")
            )
            .AllowAnonymous();
        _ = app.MapGet(
                "/full-overlay/assets/widgets.js",
                (HttpContext context) =>
                    Asset(context, FullOverlayBrowserAssets.Widgets, "text/javascript")
            )
            .AllowAnonymous();
        _ = app.MapGet(
                "/full-overlay/assets/outer.css",
                (HttpContext context) =>
                    Asset(context, FullOverlayBrowserAssets.OuterCss, "text/css")
            )
            .AllowAnonymous();
        _ = app.MapGet(
                "/full-overlay/assets/presentation.css",
                (HttpContext context) =>
                    Asset(context, OverlayBrowserSourceAssets.PresentationStylesheet, "text/css")
            )
            .AllowAnonymous();
        _ = app.MapGet(
                "/full-overlay/assets/frame",
                (HttpContext context) =>
                {
                    Headers(context, _framePolicy);
                    return Results.Content(FullOverlayBrowserAssets.FrameDocument, "text/html");
                }
            )
            .AllowAnonymous();

        var routes = app.MapGroup("/full-overlay/{accessKey}").AllowAnonymous();
        _ = routes.MapGet(
            "",
            async (
                string accessKey,
                HttpContext context,
                FullOverlayPublishedReader reader,
                CancellationToken ct
            ) =>
            {
                Headers(context, _outerPolicy);
                return await reader.ResolveAsync(accessKey, ct) is null
                    ? Results.NotFound()
                    : Document();
            }
        );
        _ = routes.MapGet(
            "/events",
            async (
                string accessKey,
                HttpContext context,
                FullOverlayPublishedReader reader,
                FullOverlayDelivery delivery,
                ILogger<FullOverlayStreamResult> logger,
                CancellationToken ct
            ) =>
            {
                Headers(context, _outerPolicy);
                var selected = await reader.ResolveAsync(accessKey, ct);
                return selected is null
                    ? Results.NotFound()
                    : new FullOverlayStreamResult(
                        delivery,
                        delivery.OpenPublished(selected),
                        logger
                    );
            }
        );
        _ = routes.MapPost(
            "/complete/{connectionId:guid}/{runId:guid}",
            async (
                string accessKey,
                Guid connectionId,
                Guid runId,
                HttpContext context,
                FullOverlayPublishedReader reader,
                FullOverlayDelivery delivery,
                CancellationToken ct
            ) =>
            {
                Headers(context, _outerPolicy);
                var selected = await reader.ResolveAsync(accessKey, ct);
                return
                    selected is not null
                    && await delivery.CompleteAsync(selected, connectionId, runId, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
        );
        MapPrivate(app);
        MapResources(app);
    }

    private static IResult Document() =>
        Results.Content(FullOverlayBrowserAssets.OuterDocument, "text/html");

    private static IResult Asset(HttpContext context, string content, string type)
    {
        Headers(context, "default-src 'none'; frame-ancestors 'none'");
        return Results.Text(content, type);
    }

    private static void Headers(HttpContext context, string policy)
    {
        if (context.Features.Get<IStatusCodePagesFeature>() is { } pages)
        {
            pages.Enabled = false;
        }
        var response = context.Response;
        response.Headers[HeaderNames.CacheControl] = "no-store, private";
        response.Headers[HeaderNames.ContentSecurityPolicy] = policy;
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    }
}
