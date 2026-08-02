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
        + "img-src 'self' data: https:; media-src 'self' https:; frame-src https:; font-src 'self'; base-uri 'none'; form-action 'none'; "
        + "frame-ancestors 'none'";
    private const string _previewContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; "
        + "img-src 'self' data: https:; media-src 'self' https:; frame-src https:; font-src 'self'; base-uri 'none'; form-action 'none'; "
        + "frame-ancestors 'self'";
    private const string _unavailableMessage = "Overlay unavailable.";

    internal static void UseOverlayAccessLogRedaction(this WebApplication app) =>
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
                                $"/overlay/{Uri.EscapeDataString(accessKey)}/media",
                                $"/overlay/{Uri.EscapeDataString(accessKey)}/cue-complete",
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
                        OverlaySnapshotProjection.GuessingV1 guessing => Results.Json(
                            guessing.Snapshot
                        ),
                        OverlaySnapshotProjection.CuePlayerV1 player => Results.Json(
                            player.Snapshot
                        ),
                        OverlaySnapshotProjection.GiveawayV1 giveaway => Results.Json(
                            giveaway.Snapshot
                        ),
                        OverlaySnapshotProjection.EventFeedV1 feed => Results.Json(feed.Snapshot),
                        _ => Unavailable(),
                    };
                }
            )
            .AllowAnonymous();
        app.MapGet(
                "/overlay/{accessKey}/media/{assetId:guid}/{contentRevision:int}",
                async (
                    HttpContext context,
                    string accessKey,
                    Guid assetId,
                    int contentRevision,
                    OverlayInstanceResolver resolver,
                    OverlayCueService cues,
                    CancellationToken cancellationToken
                ) =>
                {
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
                    var content = await cues.ResolveContentAsync(
                        resolved.Instance.HostId,
                        assetId,
                        contentRevision,
                        cancellationToken
                    );
                    return content is null ? Unavailable() : MediaFile(context, content);
                }
            )
            .AllowAnonymous();
        app.MapPost(
                "/overlay/{accessKey}/cue-complete/{runId:guid}",
                async (
                    HttpContext context,
                    string accessKey,
                    Guid runId,
                    OverlayInstanceResolver resolver,
                    OverlayCuePlaybackService playback,
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
                    await playback.CompleteAsync(
                        resolved.Instance.HostId,
                        resolved.Instance.OverlayId,
                        runId,
                        cancellationToken
                    );
                    return Results.NoContent();
                }
            )
            .DisableAntiforgery()
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
                    if (resolution is not OverlayPreviewResolution.Resolved resolved)
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
                    if (representative)
                    {
                        var sampleValue = context.Request.Query["sample"];
                        if (
                            resolved.Instance.Type is OverlayType.Guessing
                            && TryParseSample(sampleValue, out var sample)
                        )
                        {
                            suffix =
                                $"?mode=representative&sample={Uri.EscapeDataString(SampleToken(sample))}";
                        }
                        else if (
                            resolved.Instance.Type is OverlayType.Giveaway
                            && TryParseGiveawaySample(sampleValue, out var giveawaySample)
                        )
                        {
                            suffix =
                                $"?mode=representative&sample={Uri.EscapeDataString(SampleToken(giveawaySample))}";
                        }
                        else if (
                            resolved.Instance.Type is OverlayType.EventFeed
                            && TryParseEventFeedSample(sampleValue, out var eventKind)
                        )
                        {
                            suffix =
                                $"?mode=representative&sample={Uri.EscapeDataString(SampleToken(eventKind))}";
                        }
                    }
                    return Results.Text(
                        OverlayBrowserSourceDocument.Render(
                            context.Request.PathBase,
                            $"/overlays/preview/{encodedId}/state{suffix}",
                            $"/overlays/preview/{encodedId}/events",
                            $"/overlays/preview/{encodedId}/media",
                            $"/overlays/preview/{encodedId}/cue-complete",
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

                    var representative = string.Equals(
                        context.Request.Query["mode"],
                        "representative",
                        StringComparison.Ordinal
                    );
                    var projection =
                        representative
                        && resolved.Instance.Type is OverlayType.Guessing
                        && TryParseSample(context.Request.Query["sample"], out var sample)
                            ? await ProjectSampleSafelyAsync(
                                stateProvider,
                                resolved.Instance,
                                sample,
                                context.RequestServices,
                                cancellationToken
                            )
                        : representative
                        && resolved.Instance.Type is OverlayType.EventFeed
                        && TryParseEventFeedSample(
                            context.Request.Query["sample"],
                            out var eventKind
                        )
                            ? await ProjectSampleSafelyAsync(
                                stateProvider,
                                resolved.Instance,
                                eventKind,
                                context.RequestServices,
                                cancellationToken
                            )
                        : representative
                        && resolved.Instance.Type is OverlayType.Giveaway
                        && TryParseGiveawaySample(
                            context.Request.Query["sample"],
                            out var giveawaySample
                        )
                            ? await ProjectSampleSafelyAsync(
                                stateProvider,
                                resolved.Instance,
                                giveawaySample,
                                context.RequestServices,
                                cancellationToken
                            )
                        : await ProjectSafelyAsync(
                            stateProvider,
                            resolved.Instance,
                            context.RequestServices,
                            cancellationToken
                        );
                    return projection switch
                    {
                        OverlaySnapshotProjection.EmptyV1 empty => Results.Json(empty.Snapshot),
                        OverlaySnapshotProjection.GuessingV1 guessing => Results.Json(
                            guessing.Snapshot
                        ),
                        OverlaySnapshotProjection.CuePlayerV1 player => Results.Json(
                            player.Snapshot
                        ),
                        OverlaySnapshotProjection.GiveawayV1 giveaway => Results.Json(
                            giveaway.Snapshot
                        ),
                        OverlaySnapshotProjection.EventFeedV1 feed => Results.Json(feed.Snapshot),
                        _ => Unavailable(),
                    };
                }
            )
            .RequireAuthorization("HostSelected");
        app.MapGet(
                "/overlays/preview/{overlayId:guid}/media/{assetId:guid}/{contentRevision:int}",
                async (
                    HttpContext context,
                    Guid overlayId,
                    Guid assetId,
                    int contentRevision,
                    OverlayInstanceService overlays,
                    [FromServices] HostFeatureService features,
                    OverlayCueService cues,
                    CancellationToken cancellationToken
                ) =>
                {
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
                    var content = await cues.ResolveContentAsync(
                        resolved.Instance.HostId,
                        assetId,
                        contentRevision,
                        cancellationToken
                    );
                    return content is null ? Unavailable() : MediaFile(context, content);
                }
            )
            .RequireAuthorization("HostSelected");
        app.MapPost(
                "/overlays/preview/{overlayId:guid}/cue-complete/{runId:guid}",
                async (
                    HttpContext context,
                    Guid overlayId,
                    Guid runId,
                    OverlayInstanceService overlays,
                    [FromServices] HostFeatureService features,
                    OverlayCuePlaybackService playback,
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
                    await playback.CompleteAsync(
                        resolved.Instance.HostId,
                        resolved.Instance.OverlayId,
                        runId,
                        cancellationToken
                    );
                    return Results.NoContent();
                }
            )
            .DisableAntiforgery()
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
            if (result is not OverlayInstanceResult<OverlayInstanceView>.Succeeded succeeded)
            {
                return new OverlayPreviewResolution.Unavailable();
            }
            if (
                succeeded.Value.Type is OverlayType.Guessing
                && !await features.IsEnabledAsync(
                    selectedHost.Id,
                    HostFeatureFlags.Guessing,
                    cancellationToken
                )
            )
            {
                return new OverlayPreviewResolution.Unavailable();
            }
            if (
                succeeded.Value.Type is OverlayType.Giveaway
                && !await features.IsEnabledAsync(
                    selectedHost.Id,
                    HostFeatureFlags.Points,
                    cancellationToken
                )
            )
            {
                return new OverlayPreviewResolution.Unavailable();
            }

            return new OverlayPreviewResolution.Resolved(
                new ResolvedOverlayInstance(
                    selectedHost.Id,
                    succeeded.Value.Id,
                    succeeded.Value.Type,
                    succeeded.Value.Configuration,
                    succeeded.Value.Revision
                )
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

    private static async Task<OverlaySnapshotProjection> ProjectSampleSafelyAsync(
        IOverlayStateProvider stateProvider,
        ResolvedOverlayInstance instance,
        GuessingOverlaySampleState sample,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await stateProvider.ProjectSampleAsync(instance, sample, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            services
                .GetRequiredService<ILogger<OverlayBrowserSourceLog>>()
                .LogWarning(exception, "An overlay preview sample could not be projected.");
            return new OverlaySnapshotProjection.Unavailable();
        }
    }

    private static async Task<OverlaySnapshotProjection> ProjectSampleSafelyAsync(
        IOverlayStateProvider stateProvider,
        ResolvedOverlayInstance instance,
        OverlayEventFeedKind kind,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await stateProvider.ProjectEventFeedSampleAsync(
                instance,
                kind,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            services
                .GetRequiredService<ILogger<OverlayBrowserSourceLog>>()
                .LogWarning(
                    exception,
                    "An event feed overlay preview sample could not be projected."
                );
            return new OverlaySnapshotProjection.Unavailable();
        }
    }

    private static bool TryParseEventFeedSample(string? value, out OverlayEventFeedKind kind)
    {
        kind = value switch
        {
            "point-award" => OverlayEventFeedKind.PointAward,
            "guessing-winner" => OverlayEventFeedKind.GuessingWinner,
            "giveaway-winner" => OverlayEventFeedKind.GiveawayWinner,
            _ => (OverlayEventFeedKind)(-1),
        };
        return Enum.IsDefined(kind);
    }

    private static string SampleToken(OverlayEventFeedKind kind) =>
        kind switch
        {
            OverlayEventFeedKind.PointAward => "point-award",
            OverlayEventFeedKind.GuessingWinner => "guessing-winner",
            OverlayEventFeedKind.GiveawayWinner => "giveaway-winner",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static async Task<OverlaySnapshotProjection> ProjectSampleSafelyAsync(
        IOverlayStateProvider stateProvider,
        ResolvedOverlayInstance instance,
        GiveawayOverlaySampleState sample,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await stateProvider.ProjectSampleAsync(instance, sample, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            services
                .GetRequiredService<ILogger<OverlayBrowserSourceLog>>()
                .LogWarning(exception, "A giveaway overlay preview sample could not be projected.");
            return new OverlaySnapshotProjection.Unavailable();
        }
    }

    private static bool TryParseSample(string? value, out GuessingOverlaySampleState sample)
    {
        sample = value switch
        {
            "no-round" => GuessingOverlaySampleState.NoRound,
            "open" => GuessingOverlaySampleState.Open,
            "closed" => GuessingOverlaySampleState.Closed,
            "completed" => GuessingOverlaySampleState.Completed,
            _ => default,
        };
        return value is "no-round" or "open" or "closed" or "completed";
    }

    private static string SampleToken(GuessingOverlaySampleState sample) =>
        sample switch
        {
            GuessingOverlaySampleState.NoRound => "no-round",
            GuessingOverlaySampleState.Open => "open",
            GuessingOverlaySampleState.Closed => "closed",
            GuessingOverlaySampleState.Completed => "completed",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };

    private static bool TryParseGiveawaySample(string? value, out GiveawayOverlaySampleState sample)
    {
        sample = value switch
        {
            "idle" => GiveawayOverlaySampleState.Idle,
            "open" => GiveawayOverlaySampleState.Open,
            "ending" => GiveawayOverlaySampleState.Ending,
            "completed" => GiveawayOverlaySampleState.Completed,
            "cancelled" => GiveawayOverlaySampleState.Cancelled,
            _ => default,
        };
        return value is "idle" or "open" or "ending" or "completed" or "cancelled";
    }

    private static string SampleToken(GiveawayOverlaySampleState sample) =>
        sample switch
        {
            GiveawayOverlaySampleState.Idle => "idle",
            GiveawayOverlaySampleState.Open => "open",
            GiveawayOverlaySampleState.Ending => "ending",
            GiveawayOverlaySampleState.Completed => "completed",
            GiveawayOverlaySampleState.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(sample), sample, null),
        };

    private static IResult Unavailable() =>
        Results.Text(_unavailableMessage, "text/plain", statusCode: StatusCodes.Status404NotFound);

    private static IResult MediaFile(HttpContext context, OverlayMediaContent content)
    {
        context.Response.Headers[HeaderNames.CacheControl] = "private, max-age=31536000, immutable";
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        return Results.File(
            content.Path,
            content.ContentType,
            lastModified: File.GetLastWriteTimeUtc(content.Path),
            entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(
                $"\"{content.AssetId:N}-{content.ContentRevision}\""
            ),
            enableRangeProcessing: true
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
            segments.Length is < 2
            || !string.Equals(segments[0], "overlay", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[1], "assets", StringComparison.OrdinalIgnoreCase)
        )
        {
            return path;
        }

        return segments.Length == 2
            ? new PathString("/overlay/[redacted]")
            : new PathString(
                $"/overlay/[redacted]/{string.Join('/', segments.Skip(2).Select(value => value.ToLowerInvariant()))}"
            );
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
                                    or OverlayLiveTransportMessage.GuessingBaseline
                                    or OverlayLiveTransportMessage.GuessingEvent
                                    or OverlayLiveTransportMessage.CuePlayerBaseline
                                    or OverlayLiveTransportMessage.GiveawayBaseline
                                    or OverlayLiveTransportMessage.GiveawayEvent
                                    or OverlayLiveTransportMessage.EventFeedBaseline
                                    or OverlayLiveTransportMessage.EventFeedEvent
                                    or OverlayLiveTransportMessage.Cue
                                    or OverlayLiveTransportMessage.CueStop
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
                            OverlayLiveTransportMessage.GuessingBaseline baseline =>
                                JsonSerializer.Serialize(baseline.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.GuessingEvent publication =>
                                JsonSerializer.Serialize(publication.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.CuePlayerBaseline baseline =>
                                JsonSerializer.Serialize(baseline.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.GiveawayBaseline baseline =>
                                JsonSerializer.Serialize(baseline.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.GiveawayEvent publication =>
                                JsonSerializer.Serialize(publication.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.EventFeedBaseline baseline =>
                                JsonSerializer.Serialize(baseline.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.EventFeedEvent publication =>
                                JsonSerializer.Serialize(publication.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.Cue publication => JsonSerializer.Serialize(
                                publication.Envelope,
                                _jsonOptions
                            ),
                            OverlayLiveTransportMessage.CueStop publication =>
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
