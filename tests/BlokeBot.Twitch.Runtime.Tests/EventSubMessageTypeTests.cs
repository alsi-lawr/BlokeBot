using BlokeBot.Twitch.Runtime;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubMessageTypeTests
{
    [Test]
    [Arguments("session_welcome", TwitchEventSubMessageType.SessionWelcome)]
    [Arguments("session_keepalive", TwitchEventSubMessageType.SessionKeepalive)]
    [Arguments("session_reconnect", TwitchEventSubMessageType.SessionReconnect)]
    [Arguments("notification", TwitchEventSubMessageType.Notification)]
    [Arguments("revocation", TwitchEventSubMessageType.Revocation)]
    [Arguments("mystery", TwitchEventSubMessageType.Unknown)]
    [Arguments("", TwitchEventSubMessageType.Unknown)]
    public void Parses_eventsub_message_types(
        string raw,
        TwitchEventSubMessageType expectedMessageType
    )
    {
        TwitchEventSubMessageTypes.Parse(raw).ShouldBe(expectedMessageType);
    }

    [Test]
    public void Parses_missing_eventsub_message_type_as_unknown()
    {
        TwitchEventSubMessageTypes.Parse(null).ShouldBe(TwitchEventSubMessageType.Unknown);
    }
}
