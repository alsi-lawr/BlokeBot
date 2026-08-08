using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using Shouldly;

namespace BlokeBot.Site.Tests;

[NotInParallel]
public sealed class SitePresentationTests
{
    [Test]
    public async Task LiveAppUrl_InvalidOrUnsupported_FailsStartupWithActionableMessage()
    {
        foreach (var configuredValue in new[] { "/relative", "javascript:alert('unsafe')" })
        {
            await using var app = SiteApplication.Build([
                "--urls=http://127.0.0.1:0",
                $"--BlokeBotSite:LiveAppUrl={configuredValue}",
                .. SiteTestConfiguration.PrivacyArguments,
            ]);

            var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
                app.StartAsync()
            );

            exception.Message.ShouldContain(BlokeBotSiteOptionsValidation.LiveAppUrlFailure);
            await Log.CloseAndFlushAsync();
        }
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
