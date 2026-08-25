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
