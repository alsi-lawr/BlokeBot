using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchBotSettingsTests
{
    [Test]
    public void UndefinedRuntime_MappingValidatedSettings_RejectsConfiguration()
    {
        var options = new TwitchBotOptions { Runtime = (TwitchBotRuntime)99 };

        var exception = Should.Throw<OptionsValidationException>(() =>
            TwitchBotSettings.FromValidatedOptions(
                options,
                "TwitchBot",
                requireConfiguredIdentity: false
            )
        );

        exception.OptionsName.ShouldBe("TwitchBot");
        exception.Failures.ShouldContain("Twitch bot options contain an invalid value.");
    }

    [Test]
    public void CompositeOptions_MappingSettings_IsolatesRuntimeSnapshot()
    {
        var options = new TwitchBotOptions
        {
            StartupMessage = "private startup message",
            Connection = new TwitchBotConnectionOptions { Host = " irc.example.test " },
            Identity = new BotIdentityOptions
            {
                BotUsername = " TestBot ",
                ClientId = " client ",
                Scopes = ["chat:read"],
            },
        };

        var settings = TwitchBotSettings.FromOptions(options);
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
