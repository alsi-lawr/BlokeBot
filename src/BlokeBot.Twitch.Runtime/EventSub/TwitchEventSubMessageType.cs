namespace BlokeBot.Twitch.Runtime;

public enum TwitchEventSubMessageType
{
    Unknown,
    SessionWelcome,
    SessionKeepalive,
    SessionReconnect,
    Notification,
    Revocation,
}

internal static class TwitchEventSubMessageTypes
{
    public static TwitchEventSubMessageType Parse(string? messageType)
    {
        return messageType switch
        {
            "session_welcome" => TwitchEventSubMessageType.SessionWelcome,
            "session_keepalive" => TwitchEventSubMessageType.SessionKeepalive,
            "session_reconnect" => TwitchEventSubMessageType.SessionReconnect,
            "notification" => TwitchEventSubMessageType.Notification,
            "revocation" => TwitchEventSubMessageType.Revocation,
            _ => TwitchEventSubMessageType.Unknown,
        };
    }
}
