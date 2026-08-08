namespace BlokeBot.Site.Tests;

internal static class SiteTestConfiguration
{
    // Test hosts run in the default (Production) environment, where startup requires explicit
    // privacy configuration exactly as a real deployment does.
    internal static readonly string[] PrivacyArguments =
    [
        "--BlokeBotSite:ControllerName=BlokeBot (tests)",
        "--BlokeBotSite:PrivacyContact=privacy@tests.invalid",
        "--BlokeBotSite:PrivacyNoticeUrl=https://tests.invalid/privacy",
    ];
}
