using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using Microsoft.AspNetCore.Antiforgery;

namespace BlokeBot.Core.Features.Overlays.Full;

internal static partial class FullOverlayWebEndpoints
{
    private static void MapTransfer(WebApplication app) =>
        _ = app.MapPost(
                "/full-overlays/export",
                async (
                    HttpContext context,
                    IAntiforgery antiforgery,
                    FullOverlayTransferService transfer,
                    CancellationToken ct
                ) =>
                {
                    try
                    {
                        await antiforgery.ValidateRequestAsync(context);
                        var form = await context.Request.ReadFormAsync(ct);
                        var document = FullOverlayDocuments.Deserialize(
                            form["document"].ToString()
                        );
                        var prepared = await transfer.PrepareExportAsync(
                            AuthenticatedSession.FromPrincipal(context.User),
                            form["name"].ToString(),
                            document,
                            ct
                        );
                        context.Response.Headers.CacheControl = "no-store, private";
                        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                        return prepared.Match<IResult>(
                            ready =>
                                Results.File(
                                    ready.Value,
                                    "application/zip",
                                    "blokebot-full-overlay-v1.zip"
                                ),
                            rejected =>
                                Results.Text(
                                    rejected.Reason.Diagnostics[0].Message,
                                    statusCode: rejected.Reason.Kind
                                    == FullOverlayRejectionKind.Unauthorized
                                        ? 403
                                        : 422
                                )
                        );
                    }
                    catch (Exception exception)
                        when (exception
                                is AntiforgeryValidationException
                                    or JsonException
                                    or InvalidDataException
                        )
                    {
                        return Results.BadRequest("The document export request is invalid.");
                    }
                }
            )
            .RequireAuthorization("HostSelected");
}
