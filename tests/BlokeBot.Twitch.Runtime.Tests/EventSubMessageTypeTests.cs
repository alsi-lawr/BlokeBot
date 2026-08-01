using BlokeBot.Twitch.Runtime;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubMessageTypeTests
{
    [Test]
    [Arguments("session_welcome", EventSubMessageType.SessionWelcome)]
    [Arguments("session_keepalive", EventSubMessageType.SessionKeepalive)]
    [Arguments("session_reconnect", EventSubMessageType.SessionReconnect)]
    [Arguments("notification", EventSubMessageType.Notification)]
    [Arguments("revocation", EventSubMessageType.Revocation)]
    [Arguments("mystery", EventSubMessageType.Unknown)]
    [Arguments("", EventSubMessageType.Unknown)]
    public void KnownUnknownOrEmptyMessageType_Parsing_ReturnsTypedValue(
        string raw,
        EventSubMessageType expectedMessageType
    ) => EventSubMessageTypes.Parse(raw).ShouldBe(expectedMessageType);

    [Test]
    public void MissingMessageType_Parsing_ReturnsUnknown() =>
        EventSubMessageTypes.Parse(null).ShouldBe(EventSubMessageType.Unknown);
}
