using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class BotSettingsTests
{
    [Test]
    public void UndefinedRuntime_MappingValidatedSettings_RejectsConfiguration()
    {
        var options = new BotOptions
        {
            Runtime = (ChatRuntime)99,
            Identity = new BotIdentityOptions
            {
                BotUsername = "bot",
                ClientId = "client",
                ClientSecret = "secret",
                RedirectUri = "https://localhost/oauth/callback",
                Scopes = ["chat:read"],
                TokenCachePath = "tokens.json",
            },
        };

        var exception = Should.Throw<OptionsValidationException>(() =>
            BotSettings.FromConfiguredOptions(options, "TwitchBot")
        );

        exception.OptionsName.ShouldBe("TwitchBot");
        exception.Failures.ShouldContain("Twitch bot options contain an invalid value.");
    }

    [Test]
    public void CompositeOptions_MappingSettings_IsolatesRuntimeSnapshot()
    {
        var options = new BotOptions
        {
            StartupMessage = "private startup message",
            Connection = new IrcConnectionOptions { Host = " irc.example.test " },
            Identity = new BotIdentityOptions
            {
                BotUsername = " TestBot ",
                ClientId = " client ",
                Scopes = ["chat:read"],
            },
        };

        var settings = BotSettings.FromOptions(options);
        options.Connection.Host = "mutated.example.test";
        options.Identity.Scopes[0] = "user:write:chat";

        settings.Connection.Host.ShouldBe("irc.example.test");
        settings.Identity.BotUsername.ShouldBe("testbot");
        settings.Identity.ClientId.ShouldBe("client");
        settings.Identity.Scopes.ShouldBe(["chat:read"]);
        settings.ToString().ShouldNotContain("private startup message");
        settings.ToString().ShouldContain("[redacted]");
    }
}
