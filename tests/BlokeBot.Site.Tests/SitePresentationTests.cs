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
}
