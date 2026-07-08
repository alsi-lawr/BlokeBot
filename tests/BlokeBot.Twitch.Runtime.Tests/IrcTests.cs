using BlokeBot.Twitch.Runtime;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class IrcTests
{
    [Test]
    public void Parses_privmsg_with_tags()
    {
        var line =
            "@badge-info=;display-name=Alice\\sA;color=#fff :alice!alice@alice.tmi.twitch.tv PRIVMSG #channel :!deaths 5";

        var result = TwitchIrcProtocol.ParsePrivMsg(line);

        result.Success.ShouldBeTrue();
        result.Status.ShouldBe(TwitchIrcPrivMsgParseStatus.Parsed);
        var message = result.Message;
        message.Login.ShouldBe("alice");
        message.Channel.ShouldBe("channel");
        message.Text.ShouldBe("!deaths 5");
        message.RawLine.ShouldBe(line);
        message.Tags["display-name"].ShouldBe("Alice A");
        message.Tags["color"].ShouldBe("#fff");
    }

    [Test]
    public void Rejects_non_privmsg_lines()
    {
        var result = TwitchIrcProtocol.ParsePrivMsg("NOTICE #channel :hello");

        result.Success.ShouldBeFalse();
        result.Status.ShouldBe(TwitchIrcPrivMsgParseStatus.NotPrivMsg);
        result.Message.RawLine.ShouldBe("NOTICE #channel :hello");
    }

    [Test]
    public void Rejects_malformed_privmsg_lines_with_typed_status()
    {
        TwitchIrcProtocol
            .ParsePrivMsg(":missing-prefix PRIVMSG #channel :hello")
            .Status.ShouldBe(TwitchIrcPrivMsgParseStatus.MissingUserLogin);
        TwitchIrcProtocol
            .ParsePrivMsg(":a!b@c PRIVMSG channel hello")
            .Status.ShouldBe(TwitchIrcPrivMsgParseStatus.MalformedCommand);
        TwitchIrcProtocol
            .ParsePrivMsg("@ :a!b@c PRIVMSG #channel :hello")
            .Status.ShouldBe(TwitchIrcPrivMsgParseStatus.MissingTagTerminator);
    }

    [Test]
    public void Handles_ping_lines()
    {
        TwitchIrcProtocol.IsPing("PING :tmi.twitch.tv").ShouldBeTrue();
        TwitchIrcProtocol.CreatePong("PING :tmi.twitch.tv").ShouldBe("PONG :tmi.twitch.tv");
    }
}
