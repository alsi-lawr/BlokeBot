using BlokeBot.Site.Content;
using Microsoft.Extensions.Options;
using Serilog;
using Shouldly;

namespace BlokeBot.Site.Tests;

[NotInParallel]
public sealed class SitePresentationTests
{
    [Test]
    public void AutomationsGuide_ExposesStableCelTargetAndOfficialReference()
    {
        const string Route = "/automations";
        const string Anchor = "write-cel-expressions";
        var officialReference = new Uri(
            "https://github.com/cel-expr/cel-spec/blob/master/doc/intro.md"
        );

        var page = SiteGuideCatalog.All.Single(candidate => candidate.Route == Route);
        var section = page.Sections.Single(candidate => candidate.Anchor == Anchor);
        var targets = section.Links.Select(link => new Uri(link.Href, UriKind.Absolute));

        page.Route.ShouldBe(Route);
        section.Anchor.ShouldBe(Anchor);
        targets.ShouldContain(officialReference);
    }

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
