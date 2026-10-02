using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Simulation;

internal static partial class SimulationFullOverlayFixture
{
    internal sealed record Request(string Html, string Css, ImmutableArray<string> Kinds);

    internal sealed record Mutation(
        string Operation,
        string? Html,
        ImmutableArray<FullOverlayAudio> Audio = default,
        Guid? CueId = null
    );

    internal static void MapFullOverlayFixtures(this WebApplication app)
    {
        var routes = app.MapGroup("/simulation/full-overlay").RequireAuthorization("HostSelected");
        _ = routes.MapPost(
            "",
            async (
                Request request,
                HttpContext context,
                FullOverlayWidgetRegistry registry,
                FullOverlayService service,
                OverlayCueService media,
                IDbContextFactory<BlokeBotDbContext> factory,
                CancellationToken ct
            ) =>
            {
                if (!AuthCookieRequestBoundary.MayWrite(context.Request))
                {
                    return Results.Forbid();
                }
                var session = AuthenticatedSession.FromPrincipal(context.User);
                var document = FullOverlayDocument.Blank();

                await using var db = await factory.CreateDbContextAsync(ct);
                var hostId = await db
                    .Hosts.Where(host => host.Login == SimulationMode.Login)
                    .Select(host => host.Id)
                    .SingleAsync(ct);
                var widgets = request
                    .Kinds.Select(kind => registry.Create(new(kind), document.Id))
                    .OfType<FullOverlayWidget>()
                    .ToArray();
                var audio = request.Kinds.Contains("audio")
                    ? await UploadAudioAsync(media, session, ct)
                    : null;
                var cueId = audio is { } assetId
                    ? await CreateAudioCueAsync(media, session, assetId, ct)
                    : null;
                widgets = widgets
                    .Select(
                        (widget, index) =>
                            widget.Kind.Value == "audio"
                                ? widget with
                                {
                                    Configuration = JsonSerializer.SerializeToElement(
                                        new FullOverlayMediaConfiguration(audio!.Value, true)
                                    ),
                                    Audio = new(true, .3),
                                }
                            : widget.Kind.Value == "cue-player"
                                ? widget with
                                {
                                    Audio = new(index % 2 == 0, index % 2 == 0 ? .25 : .75),
                                }
                            : widget
                    )
                    .ToArray();
                var queue = await db
                    .PlayQueues.Where(queue => queue.HostId == hostId)
                    .Select(queue => queue.Id)
                    .FirstAsync(ct);
                widgets = widgets
                    .Select(widget =>
                        widget.Kind.Value == "viewer-queue"
                            ? widget with
                            {
                                Configuration = JsonSerializer.Deserialize<JsonElement>(
                                    new OverlayConfiguration.ViewerQueueV1(
                                        queue,
                                        4,
                                        4,
                                        OverlayAppearance.ViewerQueueDefault
                                    ).ToPersistenceJson()
                                ),
                            }
                            : widget
                    )
                    .ToArray();
                var anchors = string.Join(
                    "",
                    widgets.Select(widget =>
                        $"<section style='width:680px;height:340px' data-blokebot-widget='{widget.Id.Value}'></section>"
                    )
                );
                document = document with
                {
                    Html = request.Html.Replace("{{widgets}}", anchors, StringComparison.Ordinal),
                    Css = request.Css,
                    Widgets = [.. widgets],
                };
                var created = await service.CreateAsync(
                    session,
                    new("Full delivery fixture", document),
                    ct
                );
                if (created is not FullOverlayResult<FullOverlayCreation>.Succeeded value)
                {
                    return Results.BadRequest();
                }
                var published = await service.SaveAndPublishAsync(
                    session,
                    new(
                        value.Value.Overlay.Id,
                        value.Value.Overlay.Revision,
                        value.Value.Overlay.Name,
                        value.Value.Overlay.Draft
                    ),
                    ct
                );
                return published.Match<IResult>(
                    selected =>
                        Results.Json(
                            new
                            {
                                overlayId = selected.Value.Overlay.Id,
                                relativeUrl = value.Value.PrivateAccess.RelativeUrl,
                                cueId,
                                widgets = widgets.Select(widget => new
                                {
                                    id = widget.Id.Value,
                                    kind = widget.Kind.Value,
                                }),
                            }
                        ),
                    _ => Results.BadRequest()
                );
            }
        );
        _ = routes.MapPost(
            "/{overlayId:guid}",
            async (
                Guid overlayId,
                Mutation mutation,
                HttpContext context,
                FullOverlayService service,
                FullOverlayDelivery delivery,
                OverlayLiveCoordinator live,
                OverlayCuePlaybackService playback,
                IDbContextFactory<BlokeBotDbContext> factory,
                CancellationToken ct
            ) =>
            {
                if (!AuthCookieRequestBoundary.MayWrite(context.Request))
                {
                    return Results.Forbid();
                }
                var session = AuthenticatedSession.FromPrincipal(context.User);
                var result = await service.GetAsync(session, overlayId, ct);
                if (result is not FullOverlayResult<FullOverlayView>.Succeeded found)
                {
                    return Results.NotFound();
                }
                var overlay = found.Value;
                if (mutation.Operation == "observe")
                {
                    await using var db = await factory.CreateDbContextAsync(ct);
                    var hostId = await db
                        .FullOverlays.Where(row => row.PublicId == overlayId)
                        .Select(row => row.HostId)
                        .SingleAsync(ct);
                    return Results.Json(
                        new
                        {
                            presence = live.Read(hostId, overlayId).ActiveConnectionCount,
                            feedItems = await db
                                .OverlayEventFeedItems.OrderBy(row => row.Id)
                                .Select(row => new { row.Id, row.Lifecycle })
                                .ToArrayAsync(ct),
                        }
                    );
                }
                if (mutation.Operation == "cue")
                {
                    await using var db = await factory.CreateDbContextAsync(ct);
                    var hostId = await db
                        .FullOverlays.Where(row => row.PublicId == overlayId)
                        .Select(row => row.HostId)
                        .SingleAsync(ct);
                    var cue =
                        mutation.CueId
                        ?? await db
                            .OverlayCues.Where(row => row.HostId == hostId && row.IsEnabled)
                            .Select(row => row.PublicId)
                            .FirstAsync(ct);
                    var admission = await playback.AdmitAsync(
                        new(
                            hostId,
                            overlayId,
                            cue,
                            BlokeBot.Persistence.Models.OverlayCueQueuePolicy.Enqueue,
                            OverlayCueAdmissionOrigin.OwnerTest,
                            OverlayCueSafeContext.Empty
                        ),
                        ct
                    );
                    return Results.Json(
                        new
                        {
                            outcome = admission.GetType().Name,
                            runId = admission is OverlayCueAdmissionOutcome.Running running
                                ? running.RunId
                            : admission is OverlayCueAdmissionOutcome.Queued queued ? queued.RunId
                            : (Guid?)null,
                        }
                    );
                }
                var command = new FullOverlayMutation(overlayId, overlay.Revision);
                var document = overlay.Draft with
                {
                    Html = mutation.Html ?? overlay.Draft.Html,
                    Widgets = mutation.Audio.IsDefault
                        ? overlay.Draft.Widgets
                        :
                        [
                            .. overlay.Draft.Widgets.Select(
                                (widget, index) =>
                                    index < mutation.Audio.Length
                                        ? widget with
                                        {
                                            Audio = mutation.Audio[index],
                                        }
                                        : widget
                            ),
                        ],
                };
                if (mutation.Operation is "preview-sample" or "preview-live")
                {
                    return (
                        await delivery.CreatePreviewAsync(
                            session,
                            document,
                            mutation.Operation == "preview-sample"
                                ? FullOverlayDataMode.Sample
                                : FullOverlayDataMode.Live,
                            ct
                        )
                    ).Match<IResult>(
                        preview =>
                            Results.Json(
                                new { relativeUrl = $"/full-overlays/preview/{preview.Value}" }
                            ),
                        _ => Results.BadRequest()
                    );
                }
                var changed = mutation.Operation switch
                {
                    "save" => await service.SaveAsync(
                        session,
                        new(overlayId, overlay.Revision, overlay.Name, document),
                        ct
                    ),
                    "publish" => (
                        await service.SaveAndPublishAsync(
                            session,
                            new(overlayId, overlay.Revision, overlay.Name, document),
                            ct
                        )
                    ).Match<FullOverlayResult<FullOverlayView>>(
                        selected => new FullOverlayResult<FullOverlayView>.Succeeded(
                            selected.Value.Overlay
                        ),
                        rejected => new FullOverlayResult<FullOverlayView>.Rejected(rejected.Reason)
                    ),
                    "rollback" => await service.RollbackAsync(
                        session,
                        new(overlayId, overlay.Revision, new FullOverlayVersion(1)),
                        ct
                    )
                        is FullOverlayResult<FullOverlayPublicationSelection>.Succeeded rolled
                        ? new FullOverlayResult<FullOverlayView>.Succeeded(rolled.Value.Overlay)
                        : new FullOverlayResult<FullOverlayView>.Rejected(
                            new(FullOverlayRejectionKind.Invalid, [])
                        ),
                    "archive" => await service.ArchiveAsync(session, command, ct),
                    "restore" => await service.RestoreAsync(session, command, ct),
                    _ => new FullOverlayResult<FullOverlayView>.Rejected(
                        new(FullOverlayRejectionKind.Invalid, [])
                    ),
                };
                return changed.Match<IResult>(
                    selected => Results.Json(new { revision = selected.Value.Revision.Value }),
                    _ => Results.BadRequest()
                );
            }
        );
    }
}
