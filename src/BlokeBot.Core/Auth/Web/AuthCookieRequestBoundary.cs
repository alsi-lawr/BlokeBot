using Microsoft.AspNetCore.Diagnostics;

namespace BlokeBot.Core.Auth.Web;

internal static class AuthCookieRequestBoundary
{
    internal static void UseAuthCookieRequestBoundary(this WebApplication app) =>
        app.Use(
            async (context, next) =>
            {
                if (WritesAuthenticationCookies(context.Request.Path) && !MayWrite(context.Request))
                {
                    if (context.Features.Get<IStatusCodePagesFeature>() is { } pages)
                    {
                        pages.Enabled = false;
                    }
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.Headers.CacheControl = "no-store";
                    return;
                }
                await next(context);
            }
        );

    internal static bool MayWrite(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin != $"{request.Scheme}://{request.Host}")
        {
            return false;
        }
        var site = request.Headers["Sec-Fetch-Site"].ToString();
        var mode = request.Headers["Sec-Fetch-Mode"].ToString();
        var destination = request.Headers["Sec-Fetch-Dest"].ToString();
        var topLevel = mode == "navigate" && destination == "document";
        return (site == "same-origin" && destination is "" or "empty" or "document")
            || (topLevel && site == "none")
            || (origin.Length > 0 && site.Length == 0)
            || ((topLevel || site.Length == 0) && IsExistingOAuthCallback(request));
    }

    // Redirected OAuth callbacks can legitimately be cross-site without fresh user activation.
    // Reuse their existing cookie nonce; never grant the same exception to session mutations.
    private static bool IsExistingOAuthCallback(HttpRequest request)
    {
        var cookie = request.Path.Value?.TrimEnd('/').ToLowerInvariant() switch
        {
            "/auth/twitch/callback" => "BlokeBot.AuthState",
            "/oauth/channel-bot/callback" => "BlokeBot.ChannelBotState",
            _ => null,
        };
        var state = request.Query["state"].ToString();
        return cookie is not null
            && state.Length > 0
            && request.Cookies.TryGetValue(cookie, out var expected)
            && string.Equals(state, expected, StringComparison.Ordinal);
    }

    private static bool WritesAuthenticationCookies(PathString path) =>
        path.Value?.TrimEnd('/').ToLowerInvariant()
            is "/auth/login"
                or "/auth/twitch/callback"
                or "/auth/logout"
                or "/auth/select-host"
                or "/auth/select-own-host"
                or "/auth/recover-moderator-access"
                or "/auth/exit-admin"
                or "/admin/select-host"
                or "/host/create"
                or "/oauth/channel-bot/start"
                or "/oauth/channel-bot/callback";
}
