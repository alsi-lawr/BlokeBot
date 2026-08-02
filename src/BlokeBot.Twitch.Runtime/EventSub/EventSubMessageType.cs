namespace BlokeBot.Twitch.Runtime;

public enum EventSubMessageType
{
    Unknown,
    SessionWelcome,
    SessionKeepalive,
    SessionReconnect,
    Notification,
    Revocation,
}

internal static class EventSubMessageTypes
{
    public static EventSubMessageType Parse(string? messageType) =>
        messageType switch
        {
            "session_welcome" => EventSubMessageType.SessionWelcome,
            "session_keepalive" => EventSubMessageType.SessionKeepalive,
            "session_reconnect" => EventSubMessageType.SessionReconnect,
            "notification" => EventSubMessageType.Notification,
            "revocation" => EventSubMessageType.Revocation,
            _ => EventSubMessageType.Unknown,
        };
}
