using System.Diagnostics;
using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Hosts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test, Explicit]
    public async Task BrowserPreview_PreservedDiagnosticsRejectObsoleteMountCallbacks()
    {
        var driver =
            Environment.GetEnvironmentVariable("BLOKEBOT_FULL_PREVIEW_DRIVER")
            ?? throw new InvalidOperationException(
                "Set BLOKEBOT_FULL_PREVIEW_DRIVER to the owned browser driver."
            );
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        await using var app = BrowserApp(fixture, environment, runtime);
        _ = app.MapGet(
            "/fixture",
            () => Results.Json(new { frameUrl = "/full-overlay/assets/frame" })
        );
        await RunBrowserAsync(app, driver);
    }

    [Test, Explicit]
    public async Task BrowserDelivery_DeclaredRelativeAssetsAndTopLevelExecutableDocumentStayOpaque()
    {
        var driver =
            Environment.GetEnvironmentVariable("BLOKEBOT_FULL_BROWSER_DRIVER")
            ?? throw new InvalidOperationException(
                "Set BLOKEBOT_FULL_BROWSER_DRIVER to the owned browser driver."
            );
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId, browserAssets: true);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var widget = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )!;
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new(
                    "Browser package",
                    Document(
                        $"<section style='height:240px' data-blokebot-widget='{widget.Id.Value}'></section>"
                    ) with
                    {
                        Widgets = [widget],
                    }
                ),
                _ct
            )
        );
        _ = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(
                    created.Overlay.Id,
                    created.Overlay.Revision,
                    "Browser package",
                    created.Overlay.Draft
                ),
                _ct
            )
        );
        await using var app = BrowserApp(fixture, environment, runtime);
        _ = app.MapGet(
            "/fixture",
            () =>
                Results.Json(
                    new
                    {
                        relativeUrl = created.PrivateAccess.RelativeUrl,
                        document = created.Overlay.Draft,
                    }
                )
        );
        await RunBrowserAsync(app, driver);
    }

    private static WebApplication BrowserApp(
        Fixture fixture,
        WidgetEnvironment environment,
        DeliveryRuntime runtime
    )
    {
        var builder = WebApplication.CreateBuilder();
        _ = builder.Logging.ClearProviders();
        _ = builder.Services.AddSingleton(fixture.Reader);
        _ = builder.Services.AddSingleton(new OverlayInstanceResolver(fixture.Database));
        _ = builder.Services.AddSingleton(fixture.Service);
        _ = builder.Services.AddSingleton(runtime.Delivery);
        _ = builder.Services.AddSingleton(runtime.Live);
        _ = builder.Services.AddSingleton(runtime.Cues);
        _ = builder.Services.AddSingleton(environment.Media);
        _ = builder.Services.AddSingleton<IOverlayStateProvider>(environment.Sources);
        _ = builder.Services.AddAntiforgery();
        _ = builder
            .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.Cookie.Name = "BlokeBot.FullBrowserTest");
        _ = builder.Services.AddAuthorization(options =>
            options.AddPolicy(
                "HostSelected",
                policy => policy.RequireAuthenticatedUser().RequireClaim(BotHostClaims.SelectedHost)
            )
        );
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseAuthCookieRequestBoundary();
        _ = app.UseAuthentication();
        _ = app.UseAuthorization();
        app.MapOverlayBrowserSourceEndpoints();
        app.MapFullOverlayEndpoints();
        _ = app.MapGet(
            "/fixture/login",
            async (HttpContext context) =>
            {
                var host = new BotHostChoice(
                    fixture.HostId,
                    "author",
                    "Public test host",
                    AuthRole.Streamer
                );
                var principal = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [
                            new(ClaimTypes.NameIdentifier, "author-id"),
                            new(ClaimTypes.Name, "Private owner"),
                            new(AuthClaims.Login, "author"),
                            new(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(host)),
                            new(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(host)),
                        ],
                        CookieAuthenticationDefaults.AuthenticationScheme
                    )
                );
                await context.SignInAsync(principal);
                return Results.Text("Signed in fixture");
            }
        );
        _ = app.MapGet("/fixture/private", () => Results.Text("PRIVATE MANAGEMENT SENTINEL"))
            .RequireAuthorization();
        _ = app.MapGet(
            "/auth/logout",
            async (HttpContext context) =>
            {
                await context.SignOutAsync();
                return Results.Text("Signed out fixture");
            }
        );
        _ = app.MapGet(
                "/fixture/csrf",
                (HttpContext context, IAntiforgery antiforgery) =>
                    Results.Json(
                        new { token = antiforgery.GetAndStoreTokens(context).RequestToken }
                    )
            )
            .RequireAuthorization();
        return app;
    }

    private static async Task RunBrowserAsync(WebApplication app, string driver)
    {
        await app.StartAsync();
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var process = new Process
        {
            StartInfo = new("node", driver)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.Environment["FIXTURE_ORIGIN"] = address;
        process.Start().ShouldBeTrue();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            process.ExitCode.ShouldBe(0, (await output) + (await error));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await app.StopAsync();
        }
    }
}
