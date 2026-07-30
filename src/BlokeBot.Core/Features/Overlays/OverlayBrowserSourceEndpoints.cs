using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BlokeBot.Core.Features.Overlays;

internal static class OverlayBrowserSourceEndpoints
{
    private const string _contentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; "
        + "img-src 'self' data:; font-src 'self'; base-uri 'none'; form-action 'none'; "
        + "frame-ancestors 'none'";
    private const string _previewContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; "
        + "img-src 'self' data:; font-src 'self'; base-uri 'none'; form-action 'none'; "
        + "frame-ancestors 'self'";
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
                            OverlayBrowserSourceDocument.Render(
                                context.Request.PathBase,
                                $"/overlay/{Uri.EscapeDataString(accessKey)}/state",
                                $"/overlay/{Uri.EscapeDataString(accessKey)}/events",
                                OverlayBrowserSourceCredentials.Omit,
                                liveEnabled: true
                            ),
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

        app.MapGet(
                "/overlays/preview/{overlayId:guid}",
                async (
                    HttpContext context,
                    Guid overlayId,
                    OverlayInstanceService overlays,
                    [FromServices] HostFeatureService features,
                    CancellationToken cancellationToken
                ) =>
                {
                    ApplyPreviewHeaders(context.Response);
                    var resolution = await ResolvePreviewSafelyAsync(
                        context,
                        overlayId,
                        overlays,
                        features,
                        cancellationToken
                    );
                    if (resolution is not OverlayPreviewResolution.Resolved)
                    {
                        return Unavailable();
                    }

                    var encodedId = Uri.EscapeDataString(overlayId.ToString("D"));
                    var representative = string.Equals(
                        context.Request.Query["mode"],
                        "representative",
                        StringComparison.Ordinal
                    );
                    var suffix = representative ? "?mode=representative" : string.Empty;
                    return Results.Text(
                        OverlayBrowserSourceDocument.Render(
                            context.Request.PathBase,
                            $"/overlays/preview/{encodedId}/state{suffix}",
                            $"/overlays/preview/{encodedId}/events",
                            OverlayBrowserSourceCredentials.SameOrigin,
                            liveEnabled: !representative
                        ),
                        "text/html"
                    );
                }
            )
            .RequireAuthorization("HostSelected");
        app.MapGet(
                "/overlays/preview/{overlayId:guid}/state",
                async (
                    HttpContext context,
                    Guid overlayId,
                    OverlayInstanceService overlays,
                    [FromServices] HostFeatureService features,
                    IOverlayStateProvider stateProvider,
                    CancellationToken cancellationToken
                ) =>
                {
                    ApplyPreviewHeaders(context.Response);
                    var resolution = await ResolvePreviewSafelyAsync(
                        context,
                        overlayId,
                        overlays,
                        features,
                        cancellationToken
                    );
                    if (resolution is not OverlayPreviewResolution.Resolved resolved)
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
            .RequireAuthorization("HostSelected");
        app.MapGet(
                "/overlays/preview/{overlayId:guid}/events",
                async (
                    HttpContext context,
                    Guid overlayId,
                    OverlayInstanceService overlays,
                    [FromServices] HostFeatureService features,
                    OverlayLiveCoordinator live,
                    CancellationToken cancellationToken
                ) =>
                {
                    ApplyPreviewHeaders(context.Response);
                    var resolvedGeneration = live.Generation;
                    var resolution = await ResolvePreviewSafelyAsync(
                        context,
                        overlayId,
                        overlays,
                        features,
                        cancellationToken
                    );
                    if (resolution is not OverlayPreviewResolution.Resolved resolved)
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
            .RequireAuthorization("HostSelected");
    }

    private static async Task<OverlayPreviewResolution> ResolvePreviewSafelyAsync(
        HttpContext context,
        Guid overlayId,
        OverlayInstanceService overlays,
        HostFeatureService features,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var session = AuthenticatedSession.FromPrincipal(context.User);
            var selectedHost = session.State.Match<BotHostChoice?>(
                _ => null,
                selected => selected.Selection.Current,
                _ => null
            );
            if (
                selectedHost is null
                || !await features.IsEnabledAsync(
                    selectedHost.Id,
                    HostFeatureFlags.Overlays,
                    cancellationToken
                )
            )
            {
                return new OverlayPreviewResolution.Unavailable();
            }

            var result = await overlays.GetAsync(session, overlayId, cancellationToken);
            return result.Match<OverlayPreviewResolution>(
                succeeded => new OverlayPreviewResolution.Resolved(
                    new ResolvedOverlayInstance(
                        selectedHost.Id,
                        succeeded.Value.Id,
                        succeeded.Value.Type,
                        succeeded.Value.Configuration,
                        succeeded.Value.Revision
                    )
                ),
                _ => new OverlayPreviewResolution.Unavailable()
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            context
                .RequestServices.GetRequiredService<ILogger<OverlayBrowserSourceLog>>()
                .LogWarning(exception, "An authenticated overlay preview could not be resolved.");
            return new OverlayPreviewResolution.Unavailable();
        }
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

    private static void ApplyPreviewHeaders(HttpResponse response)
    {
        ApplyPrivateBrowserSourceHeaders(response);
        response.Headers[HeaderNames.ContentSecurityPolicy] = _previewContentSecurityPolicy;
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

    private abstract record OverlayPreviewResolution
    {
        private OverlayPreviewResolution() { }

        internal sealed record Resolved(ResolvedOverlayInstance Instance)
            : OverlayPreviewResolution;

        internal sealed record Unavailable : OverlayPreviewResolution;
    }
}
