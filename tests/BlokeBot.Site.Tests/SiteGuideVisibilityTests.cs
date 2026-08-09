using System.Net;
using System.Net.Http.Headers;
using BlokeBot.Site.Content;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;

namespace BlokeBot.Site.Tests;

[NotInParallel]
public sealed class SiteGuideVisibilityTests
{
    private static readonly string[] _withdrawnRoutes =
    [
        "/automations",
        "/automations/events",
        "/automations/actions",
    ];

    [Test]
    public void PublicCatalog_WithdrawsAutomationSurfaceAndRetainsDormantGuideDefinitions()
    {
        SiteRoutes.All.Intersect(_withdrawnRoutes).ShouldBeEmpty();
        SiteRoutes.GuideTopics.Intersect(_withdrawnRoutes).ShouldBeEmpty();
        SiteGuideCatalog
            .All.Select(static page => page.Route)
            .Intersect(_withdrawnRoutes)
            .ShouldBeEmpty();
        SiteGuideCatalog
            .NavigationGroups.SelectMany(static group => group.Links)
            .ShouldAllBe(static link =>
                !link.Href.StartsWith("automations", StringComparison.Ordinal)
            );
        PublicGuideText().ShouldNotContain("automation", Case.Insensitive);

        foreach (var route in _withdrawnRoutes)
        {
            var dormant = SiteGuideCatalog.Get(route);
            dormant.Route.ShouldBe(route);
            dormant.Sections.ShouldNotBeEmpty();
        }
    }

    [Test]
    public async Task PublicRoutes_HideAutomationCardsLinksAndTopics()
    {
        await using var app = SiteApplication.Build([
            "--urls=http://127.0.0.1:0",
            .. SiteTestConfiguration.PrivacyArguments,
        ]);

        try
        {
            await app.StartAsync();
            using var client = Client(app);
            var guide = await client.GetStringAsync("/guide");
            guide.ShouldNotContain("automations", Case.Insensitive);

            var ordinaryNotFound = await client.GetAsync("/withdrawn-guide-topic");
            var ordinaryNotFoundBody = await ordinaryNotFound.Content.ReadAsStringAsync();
            ordinaryNotFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);

            foreach (var route in _withdrawnRoutes)
            {
                var response = await client.GetAsync(route);
                response.StatusCode.ShouldBe(ordinaryNotFound.StatusCode);
                (await response.Content.ReadAsStringAsync()).ShouldBe(ordinaryNotFoundBody);
            }
        }
        finally
        {
            await app.StopAsync();
            await Log.CloseAndFlushAsync();
        }
    }

    private static string PublicGuideText() =>
        string.Join(
            '\n',
            SiteGuideCatalog.All.SelectMany(static page =>
                new[]
                {
                    page.Eyebrow,
                    page.Title,
                    page.Summary,
                    page.Media?.PhoneAlt,
                    page.Media?.LaptopAlt,
                    page.Media?.Caption,
                }
                    .Concat(
                        page.Sections.SelectMany(static section =>
                            new[]
                            {
                                section.Heading,
                                section.Note,
                                section.Media?.PhoneAlt,
                                section.Media?.LaptopAlt,
                                section.Media?.Caption,
                            }
                                .Concat(section.Paragraphs)
                                .Concat(section.Steps)
                                .Concat(section.Bullets)
                                .Concat(
                                    section.Links.SelectMany(static link =>
                                        new[] { link.Label, link.Href }
                                    )
                                )
                        )
                    )
                    .Concat(page.Next.SelectMany(static link => new[] { link.Label, link.Href }))
                    .OfType<string>()
            )
        );

    private static HttpClient Client(WebApplication app)
    {
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        return client;
    }
}
