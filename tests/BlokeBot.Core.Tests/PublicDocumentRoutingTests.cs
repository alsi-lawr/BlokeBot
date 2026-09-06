using System.Net;
using System.Security.Claims;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Core.Features.ViewerPortal.Boundary;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PublicDocumentRoutingTests
{
    [Test]
    [Arguments("/passports", null, false, "Channel", false)]
    [Arguments("/passports", null, false, "Channel", true)]
    [Arguments("/passports/samplechannel/me", "samplechannel", true, "cHaNnEl", true)]
    [Arguments("/passports/SampleChannel/me", "SampleChannel", true, "channel", true)]
    [Arguments("/passports/not-a-channel!/me", "not-a-channel!", true, "CHANNEL", true)]
    [Arguments("/passports/%20/me", " ", true, "Channel", true)]
    public async Task PassportRoute_HttpDocumentReachesTheSameComponentWithoutReloading(
        string path,
        string? channel,
        bool publicDocument,
        string channelKey,
        bool includeChannel
    )
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var authorization = context.AddAuthorization();
        _ = authorization.SetAuthorized("Viewer");
        _ = authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, "viewer-id"));
        var request = new DefaultHttpContext();
        request.Connection.RemoteIpAddress = IPAddress.Loopback;
        request.Request.Path = path;
        request.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "viewer-id")], "test")
        );
        if (includeChannel)
        {
            request.Request.RouteValues[channelKey] = channel;
        }
        request.SetEndpoint(
            new Endpoint(
                null,
                new EndpointMetadataCollection(
                    new ComponentTypeMetadata(typeof(ViewerPassportsPage))
                ),
                "passport"
            )
        );
        using var admission = new PublicViewerAdmission(TimeProvider.System);
        var protection = new PublicDocumentProtector(new EphemeralDataProtectionProvider());
        var reachedEndpoint = false;
        var middleware = new PublicDocumentMiddleware(_ =>
        {
            reachedEndpoint = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(request, protection, admission);
        reachedEndpoint.ShouldBeTrue();
        var bootstrap = (PublicDocumentBootstrap)
            request.Items[PublicDocumentProtector.BootstrapKey]!;
        bootstrap.Document.IsPublic.ShouldBe(publicDocument);
        PublicViewerForwarding.Applies(request).ShouldBe(publicDocument);
        using var connection = new PublicHubConnection(
            bootstrap.Document,
            publicDocument ? new PublicViewerClient(IPAddress.Loopback, "viewer-id") : null,
            () => throw new InvalidOperationException("The matching document was rejected."),
            null
        );
        request.Items[PublicHubConnection.ItemKey] = connection;
        _ = context.Services.AddSingleton(admission);
        _ = context.Services.AddSingleton<IHttpContextAccessor>(
            new HttpContextAccessor { HttpContext = request }
        );
        _ = context.Services.AddSingleton<PublicViewerCircuit>();
        context.SetRendererInfo(new RendererInfo("Server", true));
        await context
            .Services.GetRequiredService<PublicViewerCircuit>()
            .OnCircuitOpenedAsync(null!, default);
        var renderedChannels = new List<string?>();
        _ = context.ComponentFactories.AddStub<MainLayout>(parameters =>
            parameters.Get(value => value.Body)!
        );
        _ = context.ComponentFactories.AddStub<ViewerPassportsPage>(parameters =>
        {
            renderedChannels.Add(parameters.Get(value => value.Channel));
            return string.Empty;
        });
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo(path);

        var routes = context.Render<Routes>(parameters =>
            parameters
                .Add(value => value.PublicDocument, bootstrap.Document.IsPublic)
                .Add(value => value.DocumentNonce, bootstrap.Document.Nonce)
        );

        routes.WaitForAssertion(() => renderedChannels.ShouldContain(channel));
        navigation.Uri.ShouldBe($"http://localhost{path}");
        navigation.History.ShouldNotContain(entry => entry.Options.ForceLoad);
        protection.Read(bootstrap.Marker, request.User)!.IsPublic.ShouldBe(publicDocument);
    }
}
