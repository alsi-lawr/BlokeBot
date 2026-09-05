using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ViewerPortal.Boundary;
using Microsoft.AspNetCore.Diagnostics;

namespace BlokeBot.Core.Features.ViewerPassports;

internal static class ViewerPassportEndpoints
{
    public static void MapViewerPassportEndpoints(this WebApplication app) =>
        app.MapGet(
                "/passports/{channel}/export",
                async (
                    HttpContext context,
                    string channel,
                    ViewerPassportService passports,
                    PublicViewerGate publicGate,
                    TimeProvider clock,
                    CancellationToken cancellationToken
                ) =>
                {
                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (
                        !session.IsAuthenticated
                        || string.IsNullOrWhiteSpace(session.UserId)
                        || string.IsNullOrWhiteSpace(session.Login)
                    )
                    {
                        return Results.Unauthorized();
                    }
                    if (!await publicGate.TryReadAsync(channel, cancellationToken))
                    {
                        if (context.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
                        {
                            statusPages.Enabled = false;
                        }
                        context.Response.Headers.RetryAfter = "60";
                        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                    }
                    var identity = new ViewerPassportIdentity(
                        session.UserId,
                        session.Login,
                        session.DisplayText
                    );
                    var self = await passports.GetSelfAsync(channel, identity, cancellationToken);
                    if (self is not ViewerPassportQueryOutcome.Available { Passport: var passport })
                    {
                        return Results.NotFound();
                    }
                    var export = await passports.ExportAsync(
                        passport.HostId,
                        identity,
                        cancellationToken
                    );
                    if (export is not ViewerPassportExportOutcome.Succeeded succeeded)
                    {
                        return Results.NotFound();
                    }
                    context.Response.Headers.ContentDisposition =
                        $"attachment; filename=blokebot-{passport.HostLogin}-{passport.Login}-export.json";
                    return Results.Json(
                        new
                        {
                            Channel = passport.HostLogin,
                            TwitchUserId = passport.TwitchUserId,
                            ExportedAtUtc = clock.GetUtcNow(),
                            succeeded.Sections,
                        }
                    );
                }
            )
            .WithMetadata(new PublicViewerPrivateEndpoint())
            .RequireAuthorization();
}
