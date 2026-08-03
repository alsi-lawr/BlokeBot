using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class IrcTests
{
    [Test]
    public void TaggedPrivmsg_Parsing_ReturnsNormalizedMessage()
    {
        var line =
            "@badge-info=;display-name=Alice\\sA;color=#fff :alice!alice@alice.tmi.twitch.tv PRIVMSG #channel :!deaths 5";

        var result = IrcProtocol.ParsePrivMsg(line);

        var message = result.ShouldBeOfType<IrcPrivMsgParseOutcome.Parsed>().Message;
        message.Login.ShouldBe("alice");
        message.Channel.ShouldBe("channel");
        message.Text.ShouldBe("!deaths 5");
        message.RawLine.ShouldBe(line);
        message.Tags["display-name"].ShouldBe("Alice A");
        message.Tags["color"].ShouldBe("#fff");
    }

    [Test]
    public void NonPrivmsgLine_Parsing_ReturnsNotPrivmsg()
    {
        var result = IrcProtocol.ParsePrivMsg("NOTICE #channel :hello");

        _ = result.ShouldBeOfType<IrcPrivMsgParseOutcome.NotPrivMsg>();
    }

    [Test]
    public void MalformedPrivmsg_Parsing_ReturnsTypedFailureStatus()
    {
        _ = IrcProtocol
            .ParsePrivMsg(":missing-prefix PRIVMSG #channel :hello")
            .ShouldBeOfType<IrcPrivMsgParseOutcome.MissingUserLogin>();
        _ = IrcProtocol
            .ParsePrivMsg(":a!b@c PRIVMSG channel hello")
            .ShouldBeOfType<IrcPrivMsgParseOutcome.MalformedCommand>();
        _ = IrcProtocol
            .ParsePrivMsg("@ :a!b@c PRIVMSG #channel :hello")
            .ShouldBeOfType<IrcPrivMsgParseOutcome.MissingTagTerminator>();
        _ = IrcProtocol
            .ParsePrivMsg("a!b@c PRIVMSG #channel :hello")
            .ShouldBeOfType<IrcPrivMsgParseOutcome.MissingPrefix>();
        _ = IrcProtocol
            .ParsePrivMsg(": PRIVMSG #channel :hello")
            .ShouldBeOfType<IrcPrivMsgParseOutcome.MalformedPrefix>();
        _ = IrcProtocol
            .ParsePrivMsg(":a!b@c PRIVMSG #channel")
            .ShouldBeOfType<IrcPrivMsgParseOutcome.MissingChannelOrText>();
    }

    [Test]
    public void PingLine_Handling_RecognizesPingAndBuildsPong()
    {
        IrcProtocol.IsPing("PING :tmi.twitch.tv").ShouldBeTrue();
        IrcProtocol.CreatePong("PING :tmi.twitch.tv").ShouldBe("PONG :tmi.twitch.tv");
    }
}
