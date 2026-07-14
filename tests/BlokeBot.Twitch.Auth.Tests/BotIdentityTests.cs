using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class BotIdentityTests
{
    [Test]
    public void MutableScopes_MappingIdentity_NormalizesAndCopiesValues()
    {
        string[] scopes = [" User:Bot ", "chat:read", "CHAT:READ"];
        var options = ValidOptions(scopes);

        var identity = BotIdentity.FromConfiguredOptions(options, "TwitchBot.Identity");
        scopes[0] = "channel:manage:broadcast";
        options.Scopes = ["moderator:manage:announcements"];

        identity.Scopes.ShouldBe(["chat:read", "user:bot"]);
    }

    [Test]
    public void MissingRequiredIdentityValues_Validating_ReportsPropertyNamesOnly()
    {
        var options = new BotIdentityOptions
        {
            BotUsername = string.Empty,
            ClientId = string.Empty,
            ClientSecret = string.Empty,
            RedirectUri = string.Empty,
            Scopes = null!,
            TokenCachePath = string.Empty,
        };

        var result = new BotIdentityOptionsValidator().Validate("TwitchBot.Identity", options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure =>
            failure.Contains(nameof(options.Scopes), StringComparison.Ordinal)
        );
        string.Join(' ', result.Failures).ShouldNotContain("client-secret-value");
    }

    [Test]
    public void EmptyScopes_MappingPermissiveIdentity_PreservesExplicitEmptySet()
    {
        var identity = BotIdentity.FromOptions(new BotIdentityOptions { Scopes = [] });

        identity.Scopes.ShouldBeEmpty();
    }

    [Test]
    public void BlankScopes_MappingConfiguredIdentity_RejectsInvalidSet()
    {
        var options = ValidOptions([" "]);

        var exception = Should.Throw<OptionsValidationException>(() =>
            BotIdentity.FromConfiguredOptions(options, "TwitchBot.Identity")
        );

        exception.Failures.ShouldContain(failure =>
            failure.Contains(nameof(BotIdentityOptions.Scopes), StringComparison.Ordinal)
        );
    }

    [Test]
    public void IdentitySnapshot_Formatting_RedactsClientCredentials()
    {
        var identity = BotIdentity.FromConfiguredOptions(
            ValidOptions(["chat:read"]),
            "TwitchBot.Identity"
        );

        identity.ToString().ShouldNotContain("client-value");
        identity.ToString().ShouldNotContain("client-secret-value");
        identity.ToString().ShouldContain("[redacted]");
    }

    private static BotIdentityOptions ValidOptions(string[] scopes)
    {
        return new()
        {
            BotUsername = "TestBot",
            ClientId = "client-value",
            ClientSecret = "client-secret-value",
            RedirectUri = "https://localhost/oauth/callback",
            Scopes = scopes,
            TokenCachePath = "tokens.json",
        };
    }
}
