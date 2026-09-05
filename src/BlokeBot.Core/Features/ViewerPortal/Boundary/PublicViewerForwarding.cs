using System.Net;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;

namespace BlokeBot.Core.Features.ViewerPortal.Boundary;

internal static class PublicViewerForwarding
{
    internal static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        var proxies =
            configuration.GetSection("PublicViewer:ForwardedHeaders:KnownProxies").Get<string[]>()
            ?? [];
        var networks =
            configuration.GetSection("PublicViewer:ForwardedHeaders:KnownNetworks").Get<string[]>()
            ?? [];
        _ = services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                proxies.Length + networks.Length == 0
                    ? ForwardedHeaders.None
                    : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
    }

    internal static bool Applies(HttpContext context)
    {
        var type = context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>()?.Type;
        return (
                type is not null
                && PublicDocumentProtector.IsPublicPage(type, context.Request.RouteValues.Keys)
            )
            || context.GetEndpoint()?.Metadata.GetMetadata<PublicViewerPrivateEndpoint>()
                is not null
            || context.Request.Path.StartsWithSegments("/_blazor");
    }
}

internal sealed class PublicViewerPrivateEndpoint;
