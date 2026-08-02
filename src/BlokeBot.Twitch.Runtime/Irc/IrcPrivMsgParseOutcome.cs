namespace BlokeBot.Twitch.Runtime;

public abstract record IrcPrivMsgParseOutcome
{
    private IrcPrivMsgParseOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Parsed, TResult> parsed,
        Func<NotPrivMsg, TResult> notPrivMsg,
        Func<MissingTagTerminator, TResult> missingTagTerminator,
        Func<MissingPrefix, TResult> missingPrefix,
        Func<MalformedPrefix, TResult> malformedPrefix,
        Func<MissingUserLogin, TResult> missingUserLogin,
        Func<MalformedCommand, TResult> malformedCommand,
        Func<MissingChannelOrText, TResult> missingChannelOrText
    );

    public sealed record Parsed(ChatMessage Message) : IrcPrivMsgParseOutcome
    {
        public override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<NotPrivMsg, TResult> notPrivMsg,
            Func<MissingTagTerminator, TResult> missingTagTerminator,
            Func<MissingPrefix, TResult> missingPrefix,
            Func<MalformedPrefix, TResult> malformedPrefix,
            Func<MissingUserLogin, TResult> missingUserLogin,
            Func<MalformedCommand, TResult> malformedCommand,
            Func<MissingChannelOrText, TResult> missingChannelOrText
        ) => parsed(this);
    }

    public sealed record NotPrivMsg : IrcPrivMsgParseOutcome
    {
        public override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<NotPrivMsg, TResult> notPrivMsg,
            Func<MissingTagTerminator, TResult> missingTagTerminator,
            Func<MissingPrefix, TResult> missingPrefix,
            Func<MalformedPrefix, TResult> malformedPrefix,
            Func<MissingUserLogin, TResult> missingUserLogin,
            Func<MalformedCommand, TResult> malformedCommand,
            Func<MissingChannelOrText, TResult> missingChannelOrText
        ) => notPrivMsg(this);
    }

    public sealed record MissingTagTerminator : IrcPrivMsgParseOutcome
    {
        public override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<NotPrivMsg, TResult> notPrivMsg,
            Func<MissingTagTerminator, TResult> missingTagTerminator,
            Func<MissingPrefix, TResult> missingPrefix,
            Func<MalformedPrefix, TResult> malformedPrefix,
            Func<MissingUserLogin, TResult> missingUserLogin,
            Func<MalformedCommand, TResult> malformedCommand,
            Func<MissingChannelOrText, TResult> missingChannelOrText
        ) => missingTagTerminator(this);
    }

    public sealed record MissingPrefix : IrcPrivMsgParseOutcome
    {
        public override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<NotPrivMsg, TResult> notPrivMsg,
            Func<MissingTagTerminator, TResult> missingTagTerminator,
            Func<MissingPrefix, TResult> missingPrefix,
            Func<MalformedPrefix, TResult> malformedPrefix,
            Func<MissingUserLogin, TResult> missingUserLogin,
            Func<MalformedCommand, TResult> malformedCommand,
            Func<MissingChannelOrText, TResult> missingChannelOrText
        ) => missingPrefix(this);
    }

    public sealed record MalformedPrefix : IrcPrivMsgParseOutcome
    {
        public override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<NotPrivMsg, TResult> notPrivMsg,
            Func<MissingTagTerminator, TResult> missingTagTerminator,
            Func<MissingPrefix, TResult> missingPrefix,
            Func<MalformedPrefix, TResult> malformedPrefix,
            Func<MissingUserLogin, TResult> missingUserLogin,
            Func<MalformedCommand, TResult> malformedCommand,
            Func<MissingChannelOrText, TResult> missingChannelOrText
        ) => malformedPrefix(this);
    }

    public sealed record MissingUserLogin : IrcPrivMsgParseOutcome
    {
        public override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<NotPrivMsg, TResult> notPrivMsg,
            Func<MissingTagTerminator, TResult> missingTagTerminator,
            Func<MissingPrefix, TResult> missingPrefix,
            Func<MalformedPrefix, TResult> malformedPrefix,
            Func<MissingUserLogin, TResult> missingUserLogin,
            Func<MalformedCommand, TResult> malformedCommand,
            Func<MissingChannelOrText, TResult> missingChannelOrText
        ) => missingUserLogin(this);
    }

    public sealed record MalformedCommand : IrcPrivMsgParseOutcome
    {
        public override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<NotPrivMsg, TResult> notPrivMsg,
            Func<MissingTagTerminator, TResult> missingTagTerminator,
            Func<MissingPrefix, TResult> missingPrefix,
            Func<MalformedPrefix, TResult> malformedPrefix,
            Func<MissingUserLogin, TResult> missingUserLogin,
            Func<MalformedCommand, TResult> malformedCommand,
            Func<MissingChannelOrText, TResult> missingChannelOrText
        ) => malformedCommand(this);
    }

    public sealed record MissingChannelOrText : IrcPrivMsgParseOutcome
    {
        public override TResult Match<TResult>(
            Func<Parsed, TResult> parsed,
            Func<NotPrivMsg, TResult> notPrivMsg,
            Func<MissingTagTerminator, TResult> missingTagTerminator,
            Func<MissingPrefix, TResult> missingPrefix,
            Func<MalformedPrefix, TResult> malformedPrefix,
            Func<MissingUserLogin, TResult> missingUserLogin,
            Func<MalformedCommand, TResult> malformedCommand,
            Func<MissingChannelOrText, TResult> missingChannelOrText
        ) => missingChannelOrText(this);
    }
}
