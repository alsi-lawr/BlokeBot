namespace BlokeBot.Twitch.Runtime;

public enum IrcPrivMsgParseStatus
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

public sealed record IrcPrivMsgParseResult(IrcPrivMsgParseStatus Status, TwitchChatMessage Message)
{
    public bool Success => Status == IrcPrivMsgParseStatus.Parsed;
}
