using System.Net;
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

            var home = await client.GetStringAsync("/blokebot/");
            home.ShouldContain("<base href=\"/blokebot/\" />");
            home.ShouldContain("href=\"guide\"");
            home.ShouldContain("src=\"media/blokebot-banner.svg\"");
            home.ShouldContain("href=\"/blokebot/#main-content\"");
            home.ShouldContain(
                "<link rel=\"icon\" type=\"image/svg+xml\" href=\"blokebot-mark.svg\""
            );

            var dashboard = await client.GetAsync("/blokebot/dashboard");
            dashboard.StatusCode.ShouldBe(HttpStatusCode.OK);

            var stylesheet = await client.GetAsync("/blokebot/site.css");
            stylesheet.StatusCode.ShouldBe(HttpStatusCode.OK);

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
