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

public sealed record IrcPrivMsgParseResult(IrcPrivMsgParseStatus Status, ChatMessage Message)
{
    public bool Success => Status == IrcPrivMsgParseStatus.Parsed;
}
