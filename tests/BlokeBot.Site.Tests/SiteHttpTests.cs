using System.Net;
using BlokeBot.Site.Content;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Site.Tests;

[NotInParallel]
public sealed class SiteHttpTests
{
    private static readonly IReadOnlyList<string> _staticAssets =
    [
        "/site.css",
        "/media/blokebot-banner.svg",
        "/media/channel-setup.png",
        "/media/custom-commands.png",
        "/media/dashboard-home.png",
        "/media/guessing-leaderboard.png",
        "/media/guessing-workflow.webp",
        "/media/points-settings.png",
    ];

    [Test]
    public async Task EveryPublicRouteAndReferencedAsset_IsServedOverHttp()
    {
        await using var app = SiteApplication.Build(["--environment", "Development"]);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(GetListeningAddress(app)) };

        foreach (var route in SiteRoutes.All)
        {
            using var response = await client.GetAsync(route);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"Route {route} must return 200.");
            response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
            response.Headers.Contains("Set-Cookie").ShouldBeFalse();
            var content = await response.Content.ReadAsStringAsync();
            content.ShouldContain("<main id=\"main-content\"");
            content.ShouldContain("<h1");
            content.ShouldNotContain("<form", Case.Insensitive);
            content.ShouldNotContain("_framework/blazor", Case.Insensitive);
            if (route == "/install")
            {
                content.ShouldContain("Release-ready, not yet published");
                content.ShouldContain("blokebot help");
                content.ShouldContain("manual upstream review pending");
                content.ShouldContain("nix run github:alsi-lawr/BlokeBot/v0.1.0#blokebot -- serve");
                content.ShouldNotContain("github:alsi-lawr/BlokeBot#blokebot");
            }
        }

        foreach (var asset in _staticAssets)
        {
            using var response = await client.GetAsync(asset);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"Asset {asset} must return 200.");
            (await response.Content.ReadAsByteArrayAsync()).ShouldNotBeEmpty();
        }
    }

    [Test]
    public async Task HttpRequest_EmitsSerilogRequestCompletionEvent()
    {
        var sink = new CapturingSink();
        await using var app = SiteApplication.Build(
            ["--environment", "Development"],
            logging => logging.WriteTo.Sink(sink)
        );
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(GetListeningAddress(app)) };
        using var response = await client.GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        sink.Events.ShouldContain(logEvent =>
            logEvent.MessageTemplate.Text.StartsWith(
                "HTTP {RequestMethod} {RequestPath}",
                StringComparison.Ordinal
            )
        );
    }

    private static string GetListeningAddress(WebApplication app)
    {
        return app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()
                ?.Addresses.Single()
            ?? throw new InvalidOperationException("The site did not report a listening address.");
    }

    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        internal IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToArray();
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
            {
                _events.Add(logEvent);
            }
        }
    }
}
