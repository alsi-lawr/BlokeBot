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

    [Test]
    public void OnlineWebhook_MissingConfiguration_IsRejected() =>
        _ = Should.Throw<OptionsValidationException>(() =>
            BotSettings.FromConfiguredOptions(
                new BotOptions { Identity = ValidIdentity() },
                "TwitchBot"
            )
        );

    [Test]
    [Arguments("http://bot.blokebot.com/eventsub/twitch", "valid-secret")]
    [Arguments("https://bot.blokebot.com:444/eventsub/twitch", "valid-secret")]
    [Arguments("https://bot.blokebot.com/eventsub/twitch?secret=value", "valid-secret")]
    [Arguments("https://bot.blokebot.com/eventsub/twitch", "short")]
    public void OnlineWebhook_InvalidCallbackOrSecret_IsRejected(string callback, string secret)
    {
        var options = new BotOptions
        {
            Identity = ValidIdentity(),
            EventSubWebhook = new EventSubWebhookOptions
            {
                CallbackUri = new Uri(callback),
                Secret = secret,
            },
        };

        _ = Should.Throw<OptionsValidationException>(() =>
            BotSettings.FromConfiguredOptions(options, "TwitchBot")
        );
    }

    [Test]
    public void SimulationWebhook_RequiresExplicitSafeLoopbackAndRedactsValues()
    {
        var webhook = new EventSubWebhookOptions
        {
            CallbackUri = new Uri("http://127.0.0.1:5080/eventsub/twitch"),
            Secret = "deterministic-fake-secret",
        };
        var options = new BotOptions { Identity = ValidIdentity(), EventSubWebhook = webhook };

        var settings = BotSettings.FromConfiguredOptions(options, "TwitchBot", online: false);

        settings.EventSubWebhook.ShouldBeSameAs(webhook);
        webhook.ToString().ShouldNotContain(webhook.CallbackUri.AbsoluteUri);
        webhook.ToString().ShouldNotContain(webhook.Secret);
    }

    private static BotIdentityOptions ValidIdentity() =>
        new()
        {
            BotUsername = "bot",
            ClientId = "client",
            ClientSecret = "secret",
            RedirectUri = "https://localhost/oauth/callback",
            Scopes = ["chat:read"],
            TokenCachePath = "tokens.json",
        };
}
