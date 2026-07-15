using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

public abstract record IrcPrivMsgParseOutcome
{
    private IrcPrivMsgParseOutcome() { }

    public TResult Match<TResult>(
        Func<Parsed, TResult> parsed,
        Func<NotPrivMsg, TResult> notPrivMsg,
        Func<MissingTagTerminator, TResult> missingTagTerminator,
        Func<MissingPrefix, TResult> missingPrefix,
        Func<MalformedPrefix, TResult> malformedPrefix,
        Func<MissingUserLogin, TResult> missingUserLogin,
        Func<MalformedCommand, TResult> malformedCommand,
        Func<MissingChannelOrText, TResult> missingChannelOrText
    )
    {
        return this switch
        {
            Parsed value => parsed(value),
            NotPrivMsg value => notPrivMsg(value),
            MissingTagTerminator value => missingTagTerminator(value),
            MissingPrefix value => missingPrefix(value),
            MalformedPrefix value => malformedPrefix(value),
            MissingUserLogin value => missingUserLogin(value),
            MalformedCommand value => malformedCommand(value),
            MissingChannelOrText value => missingChannelOrText(value),
            _ => throw new UnreachableException("Unknown IRC private-message parse outcome."),
        };
    }

    public sealed record Parsed(ChatMessage Message) : IrcPrivMsgParseOutcome;

    public sealed record NotPrivMsg : IrcPrivMsgParseOutcome;

    public sealed record MissingTagTerminator : IrcPrivMsgParseOutcome;

    public sealed record MissingPrefix : IrcPrivMsgParseOutcome;

    public sealed record MalformedPrefix : IrcPrivMsgParseOutcome;

    public sealed record MissingUserLogin : IrcPrivMsgParseOutcome;

    public sealed record MalformedCommand : IrcPrivMsgParseOutcome;

    public sealed record MissingChannelOrText : IrcPrivMsgParseOutcome;
}
