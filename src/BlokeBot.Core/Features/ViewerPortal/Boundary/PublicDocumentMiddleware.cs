using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Diagnostics;

namespace BlokeBot.Core.Features.ViewerPortal.Boundary;

internal sealed class PublicDocumentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        PublicDocumentProtector protection,
        PublicViewerAdmission admission
    )
    {
        var path = context.Request.Path.Value?.TrimEnd('/');
        var transport =
            string.Equals(path, "/_blazor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/_blazor/negotiate", StringComparison.OrdinalIgnoreCase);
        if (transport)
        {
            var marker = context.Request.Query[PublicDocumentProtector.QueryParameter].ToString();
            var document = protection.Read(marker, context.User);
            // Consume classification before SignalR/logging; continuation parameters remain intact.
            context.Request.QueryString = Microsoft.AspNetCore.Http.QueryString.Create(
                context.Request.Query.Where(value =>
                    !string.Equals(
                        value.Key,
                        PublicDocumentProtector.QueryParameter,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
            _ = Activity.Current?.SetTag("url.query", null);
            _ = Activity.Current?.SetTag("http.target", context.Request.Path.Value);
            if (document is null)
            {
                if (context.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
                {
                    statusPages.Enabled = false;
                }
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.Headers.CacheControl = "private, no-store";
                return;
            }
            context.Items[PublicDocumentProtector.ConnectionDocumentKey] = document;
            if (document.IsPublic)
            {
                context.Response.Headers.CacheControl = "private, no-store";
                var attempt = string.Equals(
                    path,
                    "/_blazor/negotiate",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? PublicViewerAttempt.Http
                    : PublicViewerAttempt.Inbound;
                if (
                    context.Connection.RemoteIpAddress is not { } address
                    || !admission.TryAttempt(new(address, document.Subject), attempt)
                )
                {
                    await LimitedAsync(context);
                    return;
                }
            }
        }
        if (context.GetEndpoint()?.Metadata.GetMetadata<PublicViewerPrivateEndpoint>() is not null)
        {
            context.Response.Headers.CacheControl = "private, no-store, max-age=0";
            context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            if (
                context.Connection.RemoteIpAddress is not { } address
                || !admission.TryAttempt(
                    new(address, PublicDocumentProtector.Subject(context.User)),
                    PublicViewerAttempt.Http
                )
            )
            {
                await LimitedAsync(context);
                return;
            }
        }
        var component = context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>()?.Type;
        if (component is not null)
        {
            var bootstrap = protection.Create(
                PublicDocumentProtector.IsPublicPage(component, context.Request.RouteValues),
                context.User
            );
            context.Items[PublicDocumentProtector.BootstrapKey] = bootstrap;
            if (bootstrap.Document.IsPublic)
            {
                context.Response.OnStarting(() =>
                {
                    ApplyPublicHeaders(context, bootstrap.Document.Nonce);
                    return Task.CompletedTask;
                });
                if (
                    context.Connection.RemoteIpAddress is not { } address
                    || !admission.TryAttempt(
                        new(address, bootstrap.Document.Subject),
                        PublicViewerAttempt.Http
                    )
                )
                {
                    await LimitedAsync(context);
                    return;
                }
            }
        }
        await next(context);
    }

    private static async Task LimitedAsync(HttpContext context)
    {
        if (context.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
        {
            statusPages.Enabled = false;
        }
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = "60";
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("Too many requests. Wait a minute, then try again.");
    }

    private static void ApplyPublicHeaders(HttpContext context, string nonce)
    {
        var headers = context.Response.Headers;
        headers.CacheControl = "private, no-store, max-age=0";
        headers.Pragma = "no-cache";
        headers.ContentSecurityPolicy =
            $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self' data:; connect-src 'self'; frame-src 'none'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";
        headers.XFrameOptions = "SAMEORIGIN";
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["X-Robots-Tag"] =
            context.User.Identity?.IsAuthenticated == true
            || context.Response.StatusCode >= 400
            || context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null
                ? "noindex, nofollow, noarchive"
                : "noindex, follow";
    }
}
