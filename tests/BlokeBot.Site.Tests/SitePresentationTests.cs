using System.Net.Http.Headers;
using BlokeBot.Site;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Site.Tests;

[NotInParallel]
public sealed class SitePresentationTests
{
    [Test]
    public async Task LiveAppUrl_Enabled_RendersAccessibleBrandedCallToAction()
    {
        const string LiveAppUrl = "https://app.example.test/blokebot";
        await using var app = SiteApplication.Build([
            "--urls=http://127.0.0.1:0",
            $"--BlokeBotSite:LiveAppUrl={LiveAppUrl}",
        ]);

        try
        {
            await app.StartAsync();
            var home = await GetHomeAsync(app);

            home.ShouldContain(
                "<section class=\"docs-callout section-shell\" aria-labelledby=\"live-app-heading\">"
            );
            home.ShouldContain("<h2 id=\"live-app-heading\">Ready to check it out?</h2>");
            home.ShouldContain($"href=\"{LiveAppUrl}\"");
            home.ShouldContain("Check it out</a>");
        }
        finally
        {
            await app.StopAsync();
            await Log.CloseAndFlushAsync();
        }
    }

    [Test]
    public async Task LiveAppUrl_Disabled_OmitsEntireCallToAction()
    {
        foreach (var configuredValue in new string?[] { null, "", "   " })
        {
            var arguments = new List<string> { "--urls=http://127.0.0.1:0" };
            if (configuredValue is not null)
            {
                arguments.Add($"--BlokeBotSite:LiveAppUrl={configuredValue}");
            }

            await using var app = SiteApplication.Build(arguments.ToArray());
            try
            {
                await app.StartAsync();
                var home = await GetHomeAsync(app);

                home.ShouldNotContain("live-app-heading");
                home.ShouldNotContain("Ready to check it out?");
                home.ShouldNotContain("Check it out</a>");
            }
            finally
            {
                await app.StopAsync();
                await Log.CloseAndFlushAsync();
            }
        }
    }

    [Test]
    public async Task LiveAppUrl_InvalidOrUnsupported_FailsStartupWithActionableMessage()
    {
        foreach (var configuredValue in new[] { "/relative", "javascript:alert('unsafe')" })
        {
            await using var app = SiteApplication.Build([
                "--urls=http://127.0.0.1:0",
                $"--BlokeBotSite:LiveAppUrl={configuredValue}",
            ]);

            var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
                app.StartAsync()
            );

            exception.Message.ShouldContain(BlokeBotSiteOptionsValidation.LiveAppUrlFailure);
            await Log.CloseAndFlushAsync();
        }
    }

    [Test]
    public async Task Footer_RendersActualSiteProductVersion()
    {
        await using var app = SiteApplication.Build(["--urls=http://127.0.0.1:0"]);

        try
        {
            await app.StartAsync();
            var home = await GetHomeAsync(app);
            var renderedVersion = SiteProductVersion.Current.Value.Replace(
                "+",
                "&#x2B;",
                StringComparison.Ordinal
            );

            home.ShouldContain($"<p class=\"footer-meta\">Site {renderedVersion}</p>");
        }
        finally
        {
            await app.StopAsync();
            await Log.CloseAndFlushAsync();
        }
    }

    [Test]
    public void ProductVersion_TaggedAndDevelopmentBuilds_PreservePresentationIdentity()
    {
        SiteProductVersion.Display("1.2.3+build.47").ShouldBe("1.2.3");
        SiteProductVersion
            .Display("0.0.0-dev+0123456789abcdef0123456789abcdef01234567")
            .ShouldBe("0.0.0-dev+0123456789abcdef0123456789abcdef01234567");
    }

    private static async Task<string> GetHomeAsync(WebApplication app)
    {
        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        return await client.GetStringAsync("/");
    }
}
