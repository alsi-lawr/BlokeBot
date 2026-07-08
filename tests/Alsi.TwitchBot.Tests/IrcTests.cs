using Alsi.TwitchBot;
using Shouldly;
using TUnit.Core;

namespace Alsi.TwitchBot.Tests;

public sealed class IrcTests
{
    [Test]
    public void Parses_privmsg_with_tags()
    {
        var line =
            "@badge-info=;display-name=Alice\\sA;color=#fff :alice!alice@alice.tmi.twitch.tv PRIVMSG #channel :!deaths 5";

        var parsed = TwitchIrcProtocol.TryParsePrivMsg(line, out var message);

        parsed.ShouldBeTrue();
        message.Login.ShouldBe("alice");
        message.Channel.ShouldBe("channel");
        message.Text.ShouldBe("!deaths 5");
        message.RawLine.ShouldBe(line);
        message.Tags["display-name"].ShouldBe("Alice A");
        message.Tags["color"].ShouldBe("#fff");
    }

    [Test]
    public void Rejects_malformed_privmsg_lines()
    {
        TwitchIrcProtocol.TryParsePrivMsg("NOTICE #channel :hello", out _).ShouldBeFalse();
        TwitchIrcProtocol
            .TryParsePrivMsg(":missing-prefix PRIVMSG #channel :hello", out _)
            .ShouldBeFalse();
        TwitchIrcProtocol.TryParsePrivMsg(":a!b@c PRIVMSG channel hello", out _).ShouldBeFalse();
    }

    [Test]
    public void Handles_ping_lines()
    {
        TwitchIrcProtocol.IsPing("PING :tmi.twitch.tv").ShouldBeTrue();
        TwitchIrcProtocol.CreatePong("PING :tmi.twitch.tv").ShouldBe("PONG :tmi.twitch.tv");
    }
}
