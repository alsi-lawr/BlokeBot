using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace BlokeBot.Core.Features.Overlays;

internal static class OverlayBrowserSourceEndpoints
{
    private const string _contentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; "
        + "img-src 'self' data:; font-src 'self'; base-uri 'none'; form-action 'none'; "
        + "frame-ancestors 'none'";
    private const string _unavailableMessage = "Overlay unavailable.";

    internal static void UseOverlayAccessLogRedaction(this WebApplication app)
    {
        app.Use(
            async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                finally
                {
                    context.Request.Path = RedactedLogPath(context.Request.Path);
                }
            }
        );
    }

    internal static void MapOverlayBrowserSourceEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/overlay/assets/blokebot-overlay.css",
                (HttpContext context) =>
                {
                    ApplyPrivateBrowserSourceHeaders(context.Response);
                    return Results.Text(OverlayBrowserSourceAssets.Stylesheet, "text/css");
                }
            )
            .AllowAnonymous();
        app.MapGet(
                "/overlay/assets/blokebot-overlay.js",
                (HttpContext context) =>
                {
                    ApplyPrivateBrowserSourceHeaders(context.Response);
                    return Results.Text(OverlayBrowserSourceAssets.JavaScript, "text/javascript");
                }
            )
            .AllowAnonymous();
        app.MapGet(
                "/overlay/{accessKey}",
                async (
                    HttpContext context,
                    string accessKey,
                    OverlayInstanceResolver resolver,
                    CancellationToken cancellationToken
                ) =>
                {
                    ApplyPrivateBrowserSourceHeaders(context.Response);
                    var resolution = await ResolveSafelyAsync(
                        resolver,
                        accessKey,
                        context.RequestServices,
                        cancellationToken
                    );
                    return resolution is OverlayResolutionResult.Resolved
                        ? Results.Text(
                            RenderDocument(context.Request.PathBase, accessKey),
                            "text/html"
                        )
                        : Unavailable();
                }
            )
            .AllowAnonymous();
        app.MapGet(
                "/overlay/{accessKey}/events",
                async (
                    HttpContext context,
                    string accessKey,
                    OverlayInstanceResolver resolver,
                    OverlayLiveCoordinator live,
                    CancellationToken cancellationToken
                ) =>
                {
                    ApplyPrivateBrowserSourceHeaders(context.Response);
                    var resolvedGeneration = live.Generation;
                    var resolution = await ResolveSafelyAsync(
                        resolver,
                        accessKey,
                        context.RequestServices,
                        cancellationToken
                    );
                    if (resolution is not OverlayResolutionResult.Resolved resolved)
                    {
                        return Unavailable();
                    }

                    var opened = await OpenLiveSafelyAsync(
                        live,
                        resolved.Instance,
                        resolvedGeneration,
                        context.RequestServices,
                        cancellationToken
                    );
                    return opened is OverlayLiveOpenResult.Opened connected
                        ? new OverlayLiveStreamResult(live, connected.Connection)
                        : Unavailable();
                }
            )
            .AllowAnonymous();
        app.MapGet(
                "/overlay/{accessKey}/state",
                async (
                    HttpContext context,
                    string accessKey,
                    OverlayInstanceResolver resolver,
                    IOverlayStateProvider stateProvider,
                    CancellationToken cancellationToken
                ) =>
                {
                    ApplyPrivateBrowserSourceHeaders(context.Response);
                    var resolution = await ResolveSafelyAsync(
                        resolver,
                        accessKey,
                        context.RequestServices,
                        cancellationToken
                    );
                    if (resolution is not OverlayResolutionResult.Resolved resolved)
                    {
                        return Unavailable();
                    }

                    var projection = await ProjectSafelyAsync(
                        stateProvider,
                        resolved.Instance,
                        context.RequestServices,
                        cancellationToken
                    );
                    return projection switch
                    {
                        OverlaySnapshotProjection.EmptyV1 empty => Results.Json(empty.Snapshot),
                        _ => Unavailable(),
                    };
                }
            )
            .AllowAnonymous();
    }

    private static async Task<OverlayResolutionResult> ResolveSafelyAsync(
        OverlayInstanceResolver resolver,
        string accessKey,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await resolver.ResolveAsync(accessKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            services
                .GetRequiredService<ILogger<OverlayBrowserSourceLog>>()
                .LogWarning(exception, "An overlay Browser Source could not be resolved.");
            return new OverlayResolutionResult.NotFound();
        }
    }

    private static async Task<OverlayLiveOpenResult> OpenLiveSafelyAsync(
        OverlayLiveCoordinator live,
        ResolvedOverlayInstance instance,
        long resolvedGeneration,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await live.OpenAsync(instance, resolvedGeneration, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            services
                .GetRequiredService<ILogger<OverlayBrowserSourceLog>>()
                .LogWarning(exception, "An overlay live stream could not be opened.");
            return new OverlayLiveOpenResult.Unavailable();
        }
    }

    private static async Task<OverlaySnapshotProjection> ProjectSafelyAsync(
        IOverlayStateProvider stateProvider,
        ResolvedOverlayInstance instance,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await stateProvider.ProjectAsync(instance, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            services
                .GetRequiredService<ILogger<OverlayBrowserSourceLog>>()
                .LogWarning(exception, "An overlay Browser Source state could not be projected.");
            return new OverlaySnapshotProjection.Unavailable();
        }
    }

    private static IResult Unavailable()
    {
        return Results.Text(
            _unavailableMessage,
            "text/plain",
            statusCode: StatusCodes.Status404NotFound
        );
    }

    private static string RenderDocument(PathString pathBase, string accessKey)
    {
        var prefix = pathBase.HasValue ? pathBase.Value : string.Empty;
        var encodedPrefix = WebUtility.HtmlEncode(prefix);
        var encodedKey = WebUtility.HtmlEncode(Uri.EscapeDataString(accessKey));
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
              <meta name="robots" content="noindex,nofollow,noarchive">
              <title>BlokeBot overlay</title>
              <link rel="stylesheet" href="{{encodedPrefix}}/overlay/assets/blokebot-overlay.css">
            </head>
            <body>
              <main id="overlay-root" data-state-url="{{encodedPrefix}}/overlay/{{encodedKey}}/state" data-live-url="{{encodedPrefix}}/overlay/{{encodedKey}}/events" data-status="loading" aria-live="off">
                <svg id="overlay-canvas" viewBox="0 0 1920 1080" preserveAspectRatio="xMidYMid meet" aria-hidden="true" xmlns="http://www.w3.org/2000/svg"></svg>
              </main>
              <script src="{{encodedPrefix}}/overlay/assets/blokebot-overlay.js" defer></script>
            </body>
            </html>
            """;
    }

    private static void ApplyPrivateBrowserSourceHeaders(HttpResponse response)
    {
        response.Headers[HeaderNames.CacheControl] = "no-store, private";
        response.Headers[HeaderNames.Pragma] = "no-cache";
        response.Headers[HeaderNames.ContentSecurityPolicy] = _contentSecurityPolicy;
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    }

    private static PathString RedactedLogPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
        {
            return path;
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (
            segments.Length is not (2 or 3)
            || !string.Equals(segments[0], "overlay", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[1], "assets", StringComparison.OrdinalIgnoreCase)
            || segments.Length == 3
                && !string.Equals(segments[2], "state", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(segments[2], "events", StringComparison.OrdinalIgnoreCase)
        )
        {
            return path;
        }

        return segments.Length == 2
            ? new PathString("/overlay/[redacted]")
            : new PathString($"/overlay/[redacted]/{segments[2].ToLowerInvariant()}");
    }

    private sealed class OverlayLiveStreamResult(
        OverlayLiveCoordinator live,
        OverlayLiveCoordinator.OverlayLiveConnection connection
    ) : IResult
    {
        private static readonly JsonSerializerOptions _jsonOptions = new(
            JsonSerializerDefaults.Web
        );

        private static async Task WriteLiveEventAsync(
            HttpResponse response,
            string json,
            CancellationToken cancellationToken
        )
        {
            await response.WriteAsync("event: overlay\n", cancellationToken);
            await response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/event-stream; charset=utf-8";
            response.Headers[HeaderNames.CacheControl] = "no-store, private, no-transform";
            response.Headers["X-Accel-Buffering"] = "no";
            response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            try
            {
                while (!httpContext.RequestAborted.IsCancellationRequested)
                {
                    using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(
                        httpContext.RequestAborted
                    );
                    heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                    bool available;
                    try
                    {
                        available = await connection.Messages.WaitToReadAsync(heartbeat.Token);
                    }
                    catch (OperationCanceledException)
                        when (!httpContext.RequestAborted.IsCancellationRequested)
                    {
                        await response.WriteAsync(": keepalive\n\n", httpContext.RequestAborted);
                        await response.Body.FlushAsync(httpContext.RequestAborted);
                        continue;
                    }

                    if (!available)
                    {
                        if (connection.TryTakeTerminal(out var terminal))
                        {
                            await WriteLiveEventAsync(
                                response,
                                JsonSerializer.Serialize(terminal, _jsonOptions),
                                httpContext.RequestAborted
                            );
                        }
                        break;
                    }

                    while (connection.Messages.TryRead(out var message))
                    {
                        if (
                            message
                                is OverlayLiveTransportMessage.Baseline
                                    or OverlayLiveTransportMessage.Event
                            && !live.MaySend(connection)
                        )
                        {
                            continue;
                        }

                        var json = message switch
                        {
                            OverlayLiveTransportMessage.Baseline baseline =>
                                JsonSerializer.Serialize(baseline.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.Event publication =>
                                JsonSerializer.Serialize(publication.Envelope, _jsonOptions),
                            _ => string.Empty,
                        };
                        if (json.Length == 0)
                        {
                            continue;
                        }

                        await WriteLiveEventAsync(response, json, httpContext.RequestAborted);
                    }
                }
            }
            catch (OperationCanceledException)
                when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The Browser Source disconnected.
            }
            catch (Exception exception)
            {
                httpContext
                    .RequestServices.GetRequiredService<ILogger<OverlayLiveStreamLog>>()
                    .LogWarning(
                        exception,
                        "An overlay live stream ended after a transport failure of type {FailureType}.",
                        exception.GetType().Name
                    );
            }
            finally
            {
                live.Close(connection);
            }
        }
    }

    private sealed class OverlayBrowserSourceLog;

    private sealed class OverlayLiveStreamLog;
}
