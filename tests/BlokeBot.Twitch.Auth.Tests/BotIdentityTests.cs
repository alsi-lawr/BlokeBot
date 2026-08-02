using Microsoft.Extensions.Options;
using Shouldly;

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
            ClientSecret = "client-secret-value",
            RedirectUri = string.Empty,
            Scopes = null!,
            TokenCachePath = string.Empty,
        };

        var result = new BotIdentityOptionsValidator().Validate("TwitchBot.Identity", options);

        result.Failed.ShouldBeTrue();
        foreach (
            var propertyName in new[]
            {
                nameof(options.BotUsername),
                nameof(options.ClientId),
                nameof(options.RedirectUri),
                nameof(options.Scopes),
                nameof(options.TokenCachePath),
            }
        )
        {
            result.Failures.ShouldContain(failure =>
                failure.Contains(propertyName, StringComparison.Ordinal)
            );
        }

        string.Join(' ', result.Failures).ShouldNotContain(options.ClientSecret);
    }

    [Test]
    public void EmptyScopes_MappingPermissiveIdentity_RejectsInvalidSet() =>
        Should.Throw<ArgumentException>(() =>
            BotIdentity.FromOptions(new BotIdentityOptions { Scopes = [] })
        );

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

    private static BotIdentityOptions ValidOptions(string[] scopes) =>
        new()
        {
            BotUsername = "TestBot",
            ClientId = "client-value",
            ClientSecret = "client-secret-value",
            RedirectUri = "https://localhost/oauth/callback",
            Scopes = scopes,
            TokenCachePath = "tokens.json",
        };
}
