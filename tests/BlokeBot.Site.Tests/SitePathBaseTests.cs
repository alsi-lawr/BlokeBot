using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;

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
            .. SiteTestConfiguration.PrivacyArguments,
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

            var dashboard = await client.GetAsync("/blokebot/dashboard");
            dashboard.StatusCode.ShouldBe(HttpStatusCode.OK);

            var editorGuide = await client.GetStringAsync("/blokebot/full-overlays");
            editorGuide.ShouldContain("<base href=\"/blokebot/\" />");
            editorGuide.ShouldContain("href=\"full-overlays\"");
            var editorMedia = await client.GetAsync(
                "/blokebot/media/full-overlays/laptop-light-full-overlay-editor.png"
            );
            editorMedia.StatusCode.ShouldBe(HttpStatusCode.OK);
            editorMedia.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");

            var showcase = await client.GetAsync("/blokebot/blokebot-mark.svg");
            showcase.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            await app.StopAsync();
            await Log.CloseAndFlushAsync();
        }
    }
}
