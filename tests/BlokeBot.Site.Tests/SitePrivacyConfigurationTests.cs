using Microsoft.Extensions.Options;
using Serilog;
using Shouldly;

namespace BlokeBot.Site.Tests;

[NotInParallel]
public sealed class SitePrivacyConfigurationTests
{
    [Test]
    public async Task OnlineStartup_WithoutPrivacyConfiguration_IsRejected()
    {
        // The committed appsettings carry no privacy values, so a production start without
        // deployment-supplied configuration must fail rather than publish a notice with holes.
        await using var app = SiteApplication.Build(["--urls=http://127.0.0.1:0"]);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => app.StartAsync());

        exception.Message.ShouldContain(BlokeBotSiteOptionsValidation.PrivacyConfigurationFailure);
        await Log.CloseAndFlushAsync();
    }

    [Test]
    public async Task OnlineStartup_WithBlankOrInvalidPrivacyValues_IsRejected()
    {
        (string Name, string Contact, string Url)[] invalid =
        [
            ("   ", "privacy@tests.invalid", "https://tests.invalid/privacy"),
            ("BlokeBot (tests)", "not-an-address", "https://tests.invalid/privacy"),
            ("BlokeBot (tests)", "two@at@signs", "https://tests.invalid/privacy"),
            ("BlokeBot (tests)", "privacy@tests.invalid", "http://tests.invalid/privacy"),
            ("BlokeBot (tests)", "privacy@tests.invalid", "/privacy"),
        ];
        foreach (var (name, contact, url) in invalid)
        {
            await using var app = SiteApplication.Build([
                "--urls=http://127.0.0.1:0",
                $"--BlokeBotSite:ControllerName={name}",
                $"--BlokeBotSite:PrivacyContact={contact}",
                $"--BlokeBotSite:PrivacyNoticeUrl={url}",
            ]);

            var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
                app.StartAsync()
            );

            exception.Message.ShouldContain(
                BlokeBotSiteOptionsValidation.PrivacyConfigurationFailure,
                customMessage: $"{name}|{contact}|{url}"
            );
            await Log.CloseAndFlushAsync();
        }
    }

    [Test]
    public async Task DevelopmentStartup_WithoutPrivacyConfiguration_StartsWithLocalValues()
    {
        await using var app = SiteApplication.Build([
            "--urls=http://127.0.0.1:0",
            "--environment=Development",
        ]);

        try
        {
            await app.StartAsync();
        }
        finally
        {
            await app.StopAsync();
            await Log.CloseAndFlushAsync();
        }
    }
}
