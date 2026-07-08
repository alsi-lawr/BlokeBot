namespace BlokeBot.Twitch.Runtime;

public enum TwitchIrcPrivMsgParseStatus
{
    Parsed,
    NotPrivMsg,
    MissingTagTerminator,
    MissingPrefix,
    MalformedPrefix,
    MissingUserLogin,
    MalformedCommand,
    MissingChannelOrText,
}

public sealed record TwitchIrcPrivMsgParseResult(
    TwitchIrcPrivMsgParseStatus Status,
    TwitchChatMessage Message
)
{
    public bool Success => Status == TwitchIrcPrivMsgParseStatus.Parsed;
}
