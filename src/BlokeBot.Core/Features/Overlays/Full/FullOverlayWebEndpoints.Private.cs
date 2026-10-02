using BlokeBot.Core.Auth.Sessions;
using Microsoft.AspNetCore.Antiforgery;

namespace BlokeBot.Core.Features.Overlays.Full;

internal static partial class FullOverlayWebEndpoints
{
    internal sealed record PreviewRequest(FullOverlayDocument Document, FullOverlayDataMode Mode);

    private static void MapPrivate(WebApplication app)
    {
        var routes = app.MapGroup("/full-overlays").RequireAuthorization("HostSelected");
        _ = routes.MapGet(
            "/{overlayId:guid}/access",
            async (
                Guid overlayId,
                HttpContext context,
                FullOverlayService service,
                CancellationToken ct
            ) =>
            {
                Headers(context, _outerPolicy);
                return (
                    await service.ReadAccessAsync(
                        AuthenticatedSession.FromPrincipal(context.User),
                        overlayId,
                        ct
                    )
                ).Match<IResult>(
                    available => Results.Json(new { relativeUrl = available.Value.RelativeUrl }),
                    rejected =>
                        Results.StatusCode(
                            rejected.Reason.Kind == FullOverlayRejectionKind.AccessUnavailable
                                ? 409
                                : 404
                        )
                );
            }
        );
        _ = routes.MapPost(
            "/preview",
            async (
                PreviewRequest request,
                HttpContext context,
                IAntiforgery antiforgery,
                FullOverlayDelivery delivery,
                CancellationToken ct
            ) =>
            {
                Headers(context, _outerPolicy);
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }
                return (
                    await delivery.CreatePreviewAsync(
                        AuthenticatedSession.FromPrincipal(context.User),
                        request.Document,
                        request.Mode,
                        ct
                    )
                ).Match<IResult>(
                    created =>
                        Results.Json(
                            new { relativeUrl = $"/full-overlays/preview/{created.Value}" }
                        ),
                    _ => Results.NotFound()
                );
            }
        );
        _ = routes.MapGet(
            "/preview/{previewId:guid}",
            async (
                Guid previewId,
                HttpContext context,
                FullOverlayDelivery delivery,
                CancellationToken ct
            ) =>
            {
                Headers(context, _outerPolicy);
                return await delivery.MayOpenPreviewAsync(
                    previewId,
                    AuthenticatedSession.FromPrincipal(context.User),
                    ct
                )
                    ? Document()
                    : Results.NotFound();
            }
        );
        _ = routes.MapGet(
            "/preview/{previewId:guid}/events",
            async (
                Guid previewId,
                HttpContext context,
                FullOverlayDelivery delivery,
                ILogger<FullOverlayStreamResult> logger,
                CancellationToken ct
            ) =>
            {
                Headers(context, _outerPolicy);
                var lease = await delivery.OpenPreviewAsync(
                    previewId,
                    AuthenticatedSession.FromPrincipal(context.User),
                    ct
                );
                return lease is null
                    ? Results.NotFound()
                    : new FullOverlayStreamResult(delivery, lease, logger);
            }
        );
    }
}
