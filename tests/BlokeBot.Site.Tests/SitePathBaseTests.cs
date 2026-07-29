using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using BlokeBot.Site;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Site.Tests;

[NotInParallel]
public sealed class SitePathBaseTests
{
    [Test]
    public async Task ConfiguredPathBase_ServesPagesLinksAndStaticAssetsUnderPrefix()
    {
        await using var app = SiteApplication.Build([
            "--urls=http://127.0.0.1:0",
            "--BlokeBotSite:PathBase=/blokebot",
        ]);

        try
        {
            await app.StartAsync();
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.AcceptEncoding.Add(
                new StringWithQualityHeaderValue("gzip")
            );

            var home = await client.GetStringAsync("/blokebot/");
            home.ShouldContain("<base href=\"/blokebot/\" />");
            home.ShouldContain("href=\"guide\"");
            home.ShouldContain("data-theme-light-source=\"media/laptop-light-home-scroll.webp\"");
            home.ShouldContain("data-theme-dark-source=\"media/laptop-dark-home-scroll.webp\"");
            home.ShouldContain("data-theme-light-source=\"media/phone-light-home-scroll.webp\"");
            home.ShouldContain("<span><strong>BlokeBot</strong><small>Help &amp; guides</small>");
            home.ShouldContain("href=\"/blokebot/#main-content\"");
            home.ShouldContain(
                "<link rel=\"icon\" type=\"image/svg+xml\" href=\"blokebot-mark.svg\""
            );
            var stylesheetPath = Regex
                .Match(home, """<link rel="stylesheet" href="(?<path>site\.[^"]+\.css)" />""")
                .Groups["path"]
                .Value;
            stylesheetPath.ShouldNotBeEmpty();

            var dashboard = await client.GetAsync("/blokebot/dashboard");
            dashboard.StatusCode.ShouldBe(HttpStatusCode.OK);

            var nativeShoutouts = await client.GetStringAsync(
                "/blokebot/twitch-operations/shoutouts"
            );
            nativeShoutouts.ShouldContain(
                "data-theme-light-source=\"media/laptop-light-native-shoutouts.png\""
            );
            nativeShoutouts.ShouldContain("aria-label=\"Guide features\"");
            nativeShoutouts.ShouldContain("Current topic: <strong>Shoutouts</strong>");

            var serverOwners = await client.GetStringAsync("/blokebot/server-owners");
            serverOwners.ShouldContain("1. Install and run");
            serverOwners.ShouldContain("5. Custom-bot credentials");
            serverOwners.ShouldContain("ASP.NET Core manages Data Protection keys automatically");
            serverOwners.ShouldContain("DPAPI LocalMachine");
            serverOwners.ShouldContain(
                "https://github.com/alsi-lawr/BlokeBot/wiki/HTTPS-and-Reverse-Proxy"
            );
            serverOwners.ShouldContain(
                "https://github.com/alsi-lawr/BlokeBot/wiki/State-and-Secrets#custom-bot-credentials"
            );
            serverOwners.ShouldNotContain("keyring");
            serverOwners.ShouldNotContain("rotation");

            var stylesheet = await client.GetAsync($"/blokebot/{stylesheetPath}");
            stylesheet.StatusCode.ShouldBe(HttpStatusCode.OK);
            stylesheet.Content.Headers.ContentType!.MediaType.ShouldBe("text/css");
            (await stylesheet.Content.ReadAsByteArrayAsync()).ShouldNotBeEmpty();

            var showcase = await client.GetAsync("/blokebot/media/laptop-light-home-scroll.webp");
            showcase.StatusCode.ShouldBe(HttpStatusCode.OK);

            var nativeShowcase = await client.GetAsync(
                "/blokebot/media/laptop-light-native-shoutouts.png"
            );
            nativeShowcase.StatusCode.ShouldBe(HttpStatusCode.OK);

            var favicon = await client.GetAsync("/blokebot/favicon.ico");
            favicon.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await app.StopAsync();
            await Log.CloseAndFlushAsync();
        }
    }
}
