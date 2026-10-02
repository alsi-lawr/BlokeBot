using System.Collections.Immutable;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayWidgetRegistry
{
    private static ImmutableDictionary<FullOverlayWidgetKind, Registration> Builtins()
    {
        Registration Source(string kind, string name, OverlayConfiguration defaults) =>
            new(
                new(new(kind), name, defaults.GetType(), []),
                _ => SourceJson(defaults),
                (registry, context, widget, ct) =>
                    registry.ProjectSourceAsync(context, widget, defaults.Type, ct)
            );
        Registration Content<T>(
            string kind,
            string name,
            T defaults,
            Func<
                FullOverlayWidgetRegistry,
                FullOverlayRenderContext,
                FullOverlayWidget,
                T,
                CancellationToken,
                Task<FullOverlayWidgetOutput>
            > project
        )
            where T : class =>
            new(
                new(new(kind), name, typeof(T), []),
                _ => Json(defaults),
                async (registry, context, widget, ct) =>
                {
                    var configuration = FullOverlayWidgetConfigurations.Read<T>(
                        widget.Configuration
                    );
                    return configuration is null
                        ? Unavailable(widget, "invalid-configuration")
                        : await project(registry, context, widget, configuration, ct);
                }
            );
        var sources = new[]
        {
            Source("guessing", "Guessing", OverlayConfiguration.GuessingV1.Default),
            Source("cue-player", "Cue player", new OverlayConfiguration.CuePlayerV1()),
            Source("giveaway", "Giveaway", OverlayConfiguration.GiveawayV1.Default),
            new Registration(
                new(
                    new("viewer-queue"),
                    "Viewer queue",
                    typeof(OverlayConfiguration.ViewerQueueV1),
                    []
                ),
                _ =>
                    Json(
                        new
                        {
                            schemaVersion = 1,
                            queueId = 0,
                            currentRows = OverlayConfiguration.ViewerQueueV1.DefaultCurrentRows,
                            nextRows = OverlayConfiguration.ViewerQueueV1.DefaultNextRows,
                        }
                    ),
                (registry, context, widget, ct) =>
                    registry.ProjectSourceAsync(context, widget, OverlayType.ViewerQueue, ct)
            ),
            Source(
                "community-goal",
                "Community goal",
                OverlayConfiguration.CommunityGoalV1.Default
            ),
            Source(
                "viewer-funded-bounty",
                "Viewer-funded bounty",
                OverlayConfiguration.ViewerFundedBountyV1.Default
            ),
            new Registration(
                new(new("event-feed"), "Event feed", typeof(FullOverlayEventFeedConfiguration), []),
                documentId =>
                    Json(
                        new
                        {
                            bindingId = documentId,
                            feed = SourceJson(OverlayConfiguration.EventFeedV1.Default),
                        }
                    ),
                (registry, context, widget, ct) =>
                    registry.ProjectEventFeedAsync(context, widget, ct)
            ),
            Content(
                "html",
                "Isolated HTML",
                new FullOverlayHtmlConfiguration("", ""),
                (_, _, widget, config, _) =>
                    Task.FromResult<FullOverlayWidgetOutput>(
                        config.Html is null || config.Css is null
                            ? Unavailable(widget, "invalid-configuration")
                            : new FullOverlayWidgetOutput.Html(config.Html, config.Css)
                    )
            ),
            Content(
                "web",
                "Sandboxed web page",
                new FullOverlayWebConfiguration("https://example.com/"),
                (registry, _, widget, config, ct) => registry.ProjectWebAsync(widget, config, ct)
            ),
            Content(
                "image",
                "Image",
                new FullOverlayMediaConfiguration(Guid.Empty, false),
                (registry, context, widget, config, ct) =>
                    registry.ProjectMediaAsync(context, widget, config, "image/", ct)
            ),
            Content(
                "audio",
                "Audio",
                new FullOverlayMediaConfiguration(Guid.Empty, false),
                (registry, context, widget, config, ct) =>
                    registry.ProjectMediaAsync(context, widget, config, "audio/", ct)
            ),
            Content(
                "video",
                "Video",
                new FullOverlayMediaConfiguration(Guid.Empty, false),
                (registry, context, widget, config, ct) =>
                    registry.ProjectMediaAsync(context, widget, config, "video/", ct)
            ),
        };
        return sources.ToImmutableDictionary(registration => registration.Descriptor.Kind);
    }

    private async Task<FullOverlayWidgetOutput> ProjectSourceAsync(
        FullOverlayRenderContext context,
        FullOverlayWidget widget,
        OverlayType type,
        CancellationToken ct
    )
    {
        if (
            OverlayConfiguration.Parse(type, widget.Configuration.GetRawText())
            is not OverlayConfigurationParseResult.Valid valid
        )
        {
            return Unavailable(widget, "invalid-configuration");
        }
        var source = new OverlaySourceBinding(
            context.HostId,
            type,
            valid.Value,
            new(context.Version?.Value ?? 0)
        );
        if (type == OverlayType.CuePlayer)
        {
            return
                await sources.ProjectSourceAsync(source, ct)
                is OverlaySnapshotProjection.Unavailable
                ? Unavailable(widget, "source-unavailable")
                : new FullOverlayWidgetOutput.CuePlayer([
                    .. context.CuePlans.Where(plan =>
                        plan.HostId == context.HostId && plan.TargetOverlayId == context.OverlayId
                    ),
                ]);
        }
        var projection =
            context.DataMode == FullOverlayDataMode.Live
                ? await sources.ProjectSourceAsync(source, ct)
                : type switch
                {
                    OverlayType.Guessing => await sources.ProjectSampleAsync(
                        source,
                        GuessingOverlaySampleState.Open,
                        ct
                    ),
                    OverlayType.Giveaway => await sources.ProjectSampleAsync(
                        source,
                        GiveawayOverlaySampleState.Open,
                        ct
                    ),
                    OverlayType.ViewerQueue => await sources.ProjectViewerQueueSampleAsync(
                        source,
                        ViewerQueueOverlaySampleState.Open,
                        ct
                    ),
                    OverlayType.CommunityGoal or OverlayType.ViewerFundedBounty =>
                        await sources.ProjectProgressSampleAsync(
                            source,
                            ProgressOverlaySampleState.Active,
                            ct
                        ),
                    _ => new OverlaySnapshotProjection.Unavailable(),
                };
        return projection is OverlaySnapshotProjection.Unavailable
            ? Unavailable(widget, "source-unavailable")
            : new FullOverlayWidgetOutput.Source(projection);
    }

    private async Task<FullOverlayWidgetOutput> ProjectWebAsync(
        FullOverlayWidget widget,
        FullOverlayWebConfiguration config,
        CancellationToken ct
    )
    {
        if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var url))
        {
            return Unavailable(widget, "invalid-url");
        }
        var result = await urls.ValidateAsync(url, ct);
        return result is OverlayRemoteUrlDecision.Allowed
            ? new FullOverlayWidgetOutput.Web(url)
            : Unavailable(widget, "invalid-url");
    }

    private async Task<FullOverlayWidgetOutput> ProjectMediaAsync(
        FullOverlayRenderContext context,
        FullOverlayWidget widget,
        FullOverlayMediaConfiguration config,
        string mediaType,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var revision = await db
            .OverlayMediaAssets.AsNoTracking()
            .Where(asset => asset.HostId == context.HostId && asset.PublicId == config.AssetId)
            .Select(asset => (int?)asset.ContentRevision)
            .SingleOrDefaultAsync(ct);
        var content = revision is null
            ? null
            : await media.ResolveContentAsync(context.HostId, config.AssetId, revision.Value, ct);
        return
            content is null || !content.ContentType.StartsWith(mediaType, StringComparison.Ordinal)
            ? Unavailable(widget, "media-unavailable")
            : new FullOverlayWidgetOutput.Media(
                config.AssetId,
                content.ContentRevision,
                content.ContentType,
                config.Loop
            );
    }

    private async Task<FullOverlayWidgetOutput> ProjectEventFeedAsync(
        FullOverlayRenderContext context,
        FullOverlayWidget widget,
        CancellationToken ct
    )
    {
        if (!FullOverlayEventFeeds.TryConfiguration(widget, out var config))
        {
            return Unavailable(widget, "invalid-configuration");
        }
        if (FullOverlayEventFeeds.HasConflict(context.Document, config.BindingId))
        {
            return Unavailable(widget, "conflicting-feed-configuration");
        }
        var configuration = new OverlaySourceBinding(
            context.HostId,
            OverlayType.EventFeed,
            config.Feed,
            new(context.Version?.Value ?? 0)
        );
        if (context.DataMode == FullOverlayDataMode.Sample)
        {
            var projection = await sources.ProjectEventFeedSampleAsync(
                configuration,
                OverlayEventFeedKind.PointAward,
                ct
            );
            return projection is OverlaySnapshotProjection.Unavailable
                ? Unavailable(widget, "source-unavailable")
                : new FullOverlayWidgetOutput.Source(projection);
        }
        var state = await events.ProjectFullAsync(
            context.HostId,
            context.OverlayId,
            config.BindingId,
            ct
        );
        return state is null
            ? Unavailable(widget, "source-unavailable")
            : new FullOverlayWidgetOutput.Source(sources.EventFeedSnapshot(configuration, state));
    }
}
