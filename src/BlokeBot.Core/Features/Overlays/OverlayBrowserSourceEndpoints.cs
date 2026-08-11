using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace BlokeBot.Core.Features.Overlays;

internal static class OverlayBrowserSourceEndpoints
{
    private const string _contentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; "
        + "img-src 'self' data: https:; media-src 'self' https:; frame-src https:; font-src 'self'; base-uri 'none'; form-action 'none'; "
        + "frame-ancestors 'none'";
    private const string _previewContentSecurityPolicy =
        "sandbox allow-scripts allow-same-origin; default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; "
        + "img-src 'self' data: https:; media-src 'self' https:; frame-src https:; font-src 'self'; base-uri 'none'; form-action 'none'; "
        + "frame-ancestors 'self'";
    private const string _unavailableMessage = "Overlay unavailable.";

    internal static void UseOverlayAccessLogRedaction(this WebApplication app) =>
        app.Use(
            static async (context, next) =>
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
        _ = app.MapGet(
                "/overlay/assets/blokebot-overlay.css",
                static (HttpContext context) =>
                {
                    ApplyPrivateBrowserSourceHeaders(context.Response);
                    return Results.Text(OverlayBrowserSourceAssets.Stylesheet, "text/css");
                }
            )
            .AllowAnonymous();
        _ = app.MapGet(
                "/overlay/assets/blokebot-overlay.js",
                static (HttpContext context) =>
                {
                    ApplyPrivateBrowserSourceHeaders(context.Response);
                    return Results.Text(OverlayBrowserSourceAssets.JavaScript, "text/javascript");
                }
            )
            .AllowAnonymous();
        MapBrowserSourceRoutes(app, OverlaySourceKind.Public);
        MapBrowserSourceRoutes(app, OverlaySourceKind.Preview);
    }

    private static void MapBrowserSourceRoutes(WebApplication app, OverlaySourceKind source)
    {
        var routes = source switch
        {
            OverlaySourceKind.Public => app.MapGroup("/overlay/{accessKey}").AllowAnonymous(),
            OverlaySourceKind.Preview => app.MapGroup("/overlays/preview/{overlayId:guid}")
                .RequireAuthorization("HostSelected"),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
        _ = routes.MapGet(
            "",
            (HttpContext context, CancellationToken ct) => DocumentAsync(context, source, ct)
        );
        _ = routes.MapGet(
            "/appearance.css",
            (HttpContext context, CancellationToken ct) => AppearanceAsync(context, source, ct)
        );
        _ = routes.MapGet(
            "/events",
            (HttpContext context, OverlayLiveCoordinator live, CancellationToken ct) =>
                EventsAsync(context, source, live, ct)
        );
        _ = routes.MapGet(
            "/state",
            (HttpContext context, IOverlayStateProvider states, CancellationToken ct) =>
                StateAsync(context, source, states, ct)
        );
        _ = routes.MapGet(
            "/media/{assetId:guid}/{contentRevision:int}",
            (
                HttpContext context,
                Guid assetId,
                int contentRevision,
                OverlayCueService cues,
                CancellationToken ct
            ) => MediaAsync(context, source, assetId, contentRevision, cues, ct)
        );
        _ = routes
            .MapPost(
                "/cue-complete/{runId:guid}",
                (
                    HttpContext context,
                    Guid runId,
                    OverlayCuePlaybackService playback,
                    CancellationToken ct
                ) => CueCompletedAsync(context, source, runId, playback, ct)
            )
            .DisableAntiforgery();
    }

    private static async Task<IResult> DocumentAsync(
        HttpContext context,
        OverlaySourceKind source,
        CancellationToken ct
    )
    {
        ApplySourceHeaders(context.Response, source);
        var resolved = await ResolveSourceSafelyAsync(context, source, ct);
        if (resolved is null)
        {
            return Unavailable();
        }

        var sourcePath = source switch
        {
            OverlaySourceKind.Public =>
                $"/overlay/{Uri.EscapeDataString((string)context.Request.RouteValues["accessKey"]!)}",
            OverlaySourceKind.Preview =>
                $"/overlays/preview/{Guid.Parse((string)context.Request.RouteValues["overlayId"]!).ToString("D")}",
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
        var representative =
            source is OverlaySourceKind.Preview
            && string.Equals(
                context.Request.Query["mode"],
                "representative",
                StringComparison.Ordinal
            );
        var stateSuffix = representative
            ? RepresentativeSuffix(context, resolved.Type)
            : string.Empty;
        return Results.Text(
            OverlayBrowserSourceDocument.Render(
                context.Request.PathBase,
                $"{sourcePath}/state{stateSuffix}",
                $"{sourcePath}/events",
                $"{sourcePath}/media",
                $"{sourcePath}/cue-complete",
                $"{sourcePath}/appearance.css",
                source is OverlaySourceKind.Public
                    ? OverlayBrowserSourceCredentials.Omit
                    : OverlayBrowserSourceCredentials.SameOrigin,
                liveEnabled: !representative
            ),
            "text/html"
        );
    }

    private static string RepresentativeSuffix(HttpContext context, OverlayType type)
    {
        var sample = context.Request.Query["sample"];
        var sampleToken = type switch
        {
            OverlayType.Guessing when TryParseSample(sample, out var value) => SampleToken(value),
            OverlayType.Giveaway when TryParseGiveawaySample(sample, out var value) => SampleToken(
                value
            ),
            OverlayType.EventFeed when TryParseEventFeedSample(sample, out var value) =>
                SampleToken(value),
            OverlayType.ViewerQueue when TryParseViewerQueueSample(sample, out var value) =>
                SampleToken(value),
            OverlayType.CommunityGoal
            or OverlayType.ViewerFundedBounty when TryParseProgressSample(sample, out var value) =>
                SampleToken(value),
            _ => null,
        };
        return sampleToken is null
            ? "?mode=representative"
            : $"?mode=representative&sample={Uri.EscapeDataString(sampleToken)}";
    }

    private static async Task<IResult> AppearanceAsync(
        HttpContext context,
        OverlaySourceKind source,
        CancellationToken ct
    )
    {
        ApplySourceHeaders(context.Response, source);
        var resolved = await ResolveSourceSafelyAsync(context, source, ct);
        return resolved is null
            ? Unavailable()
            : Results.Text(AppearanceCss(resolved.Configuration), "text/css");
    }

    private static async Task<IResult> EventsAsync(
        HttpContext context,
        OverlaySourceKind source,
        OverlayLiveCoordinator live,
        CancellationToken ct
    )
    {
        ApplySourceHeaders(context.Response, source);
        var resolvedGeneration = live.Generation;
        var resolved = await ResolveSourceSafelyAsync(context, source, ct);
        if (resolved is null)
        {
            return Unavailable();
        }

        var opened = await OpenLiveSafelyAsync(
            live,
            resolved,
            resolvedGeneration,
            context.RequestServices,
            ct
        );
        return opened is OverlayLiveOpenResult.Opened connected
            ? new OverlayLiveStreamResult(live, connected.Connection)
            : Unavailable();
    }

    private static async Task<IResult> StateAsync(
        HttpContext context,
        OverlaySourceKind source,
        IOverlayStateProvider stateProvider,
        CancellationToken ct
    )
    {
        ApplySourceHeaders(context.Response, source);
        var resolved = await ResolveSourceSafelyAsync(context, source, ct);
        if (resolved is null)
        {
            return Unavailable();
        }

        var projection =
            source is OverlaySourceKind.Preview
            && string.Equals(
                context.Request.Query["mode"],
                "representative",
                StringComparison.Ordinal
            )
                ? await ProjectRepresentativeSafelyAsync(context, stateProvider, resolved, ct)
                : await ProjectSafelyAsync(stateProvider, resolved, context.RequestServices, ct);
        return Snapshot(projection);
    }

    private static Task<OverlaySnapshotProjection> ProjectRepresentativeSafelyAsync(
        HttpContext context,
        IOverlayStateProvider stateProvider,
        ResolvedOverlayInstance instance,
        CancellationToken ct
    ) =>
        (instance.Type, context.Request.Query["sample"].ToString()) switch
        {
            (OverlayType.Guessing, var sample) when TryParseSample(sample, out var value) =>
                ProjectSampleSafelyAsync(
                    stateProvider,
                    instance,
                    value,
                    context.RequestServices,
                    ct
                ),
            (OverlayType.Giveaway, var sample) when TryParseGiveawaySample(sample, out var value) =>
                ProjectSampleSafelyAsync(
                    stateProvider,
                    instance,
                    value,
                    context.RequestServices,
                    ct
                ),
            (OverlayType.EventFeed, var sample)
                when TryParseEventFeedSample(sample, out var value) => ProjectSampleSafelyAsync(
                stateProvider,
                instance,
                value,
                context.RequestServices,
                ct
            ),
            (OverlayType.ViewerQueue, var sample)
                when TryParseViewerQueueSample(sample, out var value) => ProjectSampleSafelyAsync(
                stateProvider,
                instance,
                value,
                context.RequestServices,
                ct
            ),
            (OverlayType.CommunityGoal or OverlayType.ViewerFundedBounty, var sample)
                when TryParseProgressSample(sample, out var value) => ProjectSampleSafelyAsync(
                stateProvider,
                instance,
                value,
                context.RequestServices,
                ct
            ),
            _ => ProjectSafelyAsync(stateProvider, instance, context.RequestServices, ct),
        };

    private static IResult Snapshot(OverlaySnapshotProjection projection) =>
        projection switch
        {
            OverlaySnapshotProjection.EmptyV1 empty => Results.Json(empty.Snapshot),
            OverlaySnapshotProjection.GuessingV1 guessing => Results.Json(guessing.Snapshot),
            OverlaySnapshotProjection.CuePlayerV1 player => Results.Json(player.Snapshot),
            OverlaySnapshotProjection.GiveawayV1 giveaway => Results.Json(giveaway.Snapshot),
            OverlaySnapshotProjection.EventFeedV1 feed => Results.Json(feed.Snapshot),
            OverlaySnapshotProjection.ViewerQueueV1 queue => Results.Json(queue.Snapshot),
            OverlaySnapshotProjection.CommunityGoalV1 goal => Results.Json(goal.Snapshot),
            OverlaySnapshotProjection.ViewerFundedBountyV1 bounty => Results.Json(bounty.Snapshot),
            _ => Unavailable(),
        };

    private static async Task<IResult> MediaAsync(
        HttpContext context,
        OverlaySourceKind source,
        Guid assetId,
        int contentRevision,
        OverlayCueService cues,
        CancellationToken ct
    )
    {
        var resolved = await ResolveSourceSafelyAsync(context, source, ct);
        if (resolved is null)
        {
            return Unavailable();
        }

        var content = await cues.ResolveContentAsync(resolved.HostId, assetId, contentRevision, ct);
        return content is null ? Unavailable() : MediaFile(context, content);
    }

    private static async Task<IResult> CueCompletedAsync(
        HttpContext context,
        OverlaySourceKind source,
        Guid runId,
        OverlayCuePlaybackService playback,
        CancellationToken ct
    )
    {
        ApplySourceHeaders(context.Response, source);
        var resolved = await ResolveSourceSafelyAsync(context, source, ct);
        if (resolved is null)
        {
            return Unavailable();
        }

        _ = await playback.CompleteAsync(resolved.HostId, resolved.OverlayId, runId, ct);
        return Results.NoContent();
    }

    private static async Task<ResolvedOverlayInstance?> ResolveSourceSafelyAsync(
        HttpContext context,
        OverlaySourceKind source,
        CancellationToken ct
    ) =>
        source switch
        {
            OverlaySourceKind.Public => await ResolveSafelyAsync(
                context.RequestServices.GetRequiredService<OverlayInstanceResolver>(),
                (string)context.Request.RouteValues["accessKey"]!,
                context.RequestServices,
                ct
            )
                is OverlayResolutionResult.Resolved resolved
                ? resolved.Instance
                : null,
            OverlaySourceKind.Preview => await ResolvePreviewSafelyAsync(
                context,
                Guid.Parse((string)context.Request.RouteValues["overlayId"]!),
                context.RequestServices.GetRequiredService<OverlayInstanceService>(),
                context.RequestServices.GetRequiredService<HostFeatureService>(),
                ct
            )
                is OverlayPreviewResolution.Resolved resolved
                ? resolved.Instance
                : null,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private static void ApplySourceHeaders(HttpResponse response, OverlaySourceKind source)
    {
        if (source is OverlaySourceKind.Preview)
        {
            ApplyPreviewHeaders(response);
        }
        else
        {
            ApplyPrivateBrowserSourceHeaders(response);
        }
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
                static _ => null,
                static selected => selected.Selection.Current,
                static _ => null
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
            return
                result is OverlayInstanceResult<OverlayInstanceView>.Succeeded succeeded
                && await features.IsEnabledAsync(
                    selectedHost.Id,
                    OverlayRequiredFeatures.For(succeeded.Value.Type),
                    cancellationToken
                )
                ? new OverlayPreviewResolution.Resolved(
                    new ResolvedOverlayInstance(
                        selectedHost.Id,
                        succeeded.Value.Id,
                        succeeded.Value.Type,
                        succeeded.Value.Configuration,
                        succeeded.Value.Revision
                    )
                )
                : new OverlayPreviewResolution.Unavailable();
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

    private static string AppearanceCss(OverlayConfiguration configuration)
    {
        var appearance = configuration switch
        {
            OverlayConfiguration.GuessingV1 guessing => guessing.Appearance,
            OverlayConfiguration.GiveawayV1 giveaway => giveaway.Appearance,
            OverlayConfiguration.EventFeedV1 feed => feed.Appearance,
            OverlayConfiguration.ViewerQueueV1 queue => queue.Appearance,
            OverlayConfiguration.ProgressOverlayV1 progress => progress.Appearance,
            _ => null,
        };
        return appearance?.ToScopedCss() ?? string.Empty;
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
        ViewerQueueOverlaySampleState sample,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await stateProvider.ProjectViewerQueueSampleAsync(
                instance,
                sample,
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
                .LogWarning(exception, "A Viewer Queue sample could not be projected.");
            return new OverlaySnapshotProjection.Unavailable();
        }
    }

    private static async Task<OverlaySnapshotProjection> ProjectSampleSafelyAsync(
        IOverlayStateProvider stateProvider,
        ResolvedOverlayInstance instance,
        ProgressOverlaySampleState sample,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await stateProvider.ProjectProgressSampleAsync(
                instance,
                sample,
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
                .LogWarning(exception, "A progress overlay sample could not be projected.");
            return new OverlaySnapshotProjection.Unavailable();
        }
    }

    private static bool TryParseProgressSample(string? value, out ProgressOverlaySampleState sample)
    {
        sample = value switch
        {
            "active" => ProgressOverlaySampleState.Active,
            "progress-update" => ProgressOverlaySampleState.ProgressUpdate,
            "completed" => ProgressOverlaySampleState.Completed,
            "failed" => ProgressOverlaySampleState.Failed,
            "expired" => ProgressOverlaySampleState.Expired,
            "empty" => ProgressOverlaySampleState.Empty,
            _ => default,
        };
        return value
            is "active"
                or "progress-update"
                or "completed"
                or "failed"
                or "expired"
                or "empty";
    }

    private static string SampleToken(ProgressOverlaySampleState sample) =>
        sample switch
        {
            ProgressOverlaySampleState.Active => "active",
            ProgressOverlaySampleState.ProgressUpdate => "progress-update",
            ProgressOverlaySampleState.Completed => "completed",
            ProgressOverlaySampleState.Failed => "failed",
            ProgressOverlaySampleState.Expired => "expired",
            ProgressOverlaySampleState.Empty => "empty",
            _ => throw new ArgumentOutOfRangeException(nameof(sample)),
        };

    private static bool TryParseViewerQueueSample(
        string? value,
        out ViewerQueueOverlaySampleState sample
    )
    {
        sample = value switch
        {
            "open" => ViewerQueueOverlaySampleState.Open,
            "closed" => ViewerQueueOverlaySampleState.Closed,
            "party-changed" => ViewerQueueOverlaySampleState.PartyChanged,
            "ready-outcome" => ViewerQueueOverlaySampleState.ReadyOutcome,
            "selected-next" => ViewerQueueOverlaySampleState.SelectedNext,
            _ => default,
        };
        return value is "open" or "closed" or "party-changed" or "ready-outcome" or "selected-next";
    }

    private static string SampleToken(ViewerQueueOverlaySampleState sample) =>
        sample switch
        {
            ViewerQueueOverlaySampleState.Open => "open",
            ViewerQueueOverlaySampleState.Closed => "closed",
            ViewerQueueOverlaySampleState.PartyChanged => "party-changed",
            ViewerQueueOverlaySampleState.ReadyOutcome => "ready-outcome",
            ViewerQueueOverlaySampleState.SelectedNext => "selected-next",
            _ => throw new ArgumentOutOfRangeException(nameof(sample)),
        };

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
            "bingo-event" => OverlayEventFeedKind.BingoEvent,
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
            OverlayEventFeedKind.BingoEvent => "bingo-event",
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
        context.Response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox; default-src 'none'";
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
        return (
            segments.Length < 2
            || !string.Equals(segments[0], "overlay", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[1], "assets", StringComparison.OrdinalIgnoreCase)
        ) switch
        {
            true => path,
            false => segments.Length switch
            {
                2 => new PathString("/overlay/[redacted]"),
                _ => new PathString(
                    $"/overlay/[redacted]/{string.Join('/', segments.Skip(2).Select(static value => value.ToLowerInvariant()))}"
                ),
            },
        };
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
                                    or OverlayLiveTransportMessage.ViewerQueueBaseline
                                    or OverlayLiveTransportMessage.ViewerQueueEvent
                                    or OverlayLiveTransportMessage.CommunityGoalBaseline
                                    or OverlayLiveTransportMessage.CommunityGoalEvent
                                    or OverlayLiveTransportMessage.ViewerFundedBountyBaseline
                                    or OverlayLiveTransportMessage.ViewerFundedBountyEvent
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
                            OverlayLiveTransportMessage.ViewerQueueBaseline baseline =>
                                JsonSerializer.Serialize(baseline.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.ViewerQueueEvent publication =>
                                JsonSerializer.Serialize(publication.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.CommunityGoalBaseline baseline =>
                                JsonSerializer.Serialize(baseline.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.CommunityGoalEvent publication =>
                                JsonSerializer.Serialize(publication.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.ViewerFundedBountyBaseline baseline =>
                                JsonSerializer.Serialize(baseline.Envelope, _jsonOptions),
                            OverlayLiveTransportMessage.ViewerFundedBountyEvent publication =>
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

    private enum OverlaySourceKind
    {
        Public,
        Preview,
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
