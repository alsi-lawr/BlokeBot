namespace BlokeBot.Core.Features.HostedChannels.Authorization;

public abstract record WhisperResponseConfigurationOutcome
{
    private WhisperResponseConfigurationOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Configured, TResult> configured,
        Func<HostNotFound, TResult> hostNotFound,
        Func<CustomBotRequired, TResult> customBotRequired
    );

    public sealed record Configured : WhisperResponseConfigurationOutcome
    {
        public override TResult Match<TResult>(
            Func<Configured, TResult> configured,
            Func<HostNotFound, TResult> hostNotFound,
            Func<CustomBotRequired, TResult> customBotRequired
        )
        {
            return configured(this);
        }
    }

    public sealed record HostNotFound : WhisperResponseConfigurationOutcome
    {
        public override TResult Match<TResult>(
            Func<Configured, TResult> configured,
            Func<HostNotFound, TResult> hostNotFound,
            Func<CustomBotRequired, TResult> customBotRequired
        )
        {
            return hostNotFound(this);
        }
    }

    public sealed record CustomBotRequired : WhisperResponseConfigurationOutcome
    {
        public override TResult Match<TResult>(
            Func<Configured, TResult> configured,
            Func<HostNotFound, TResult> hostNotFound,
            Func<CustomBotRequired, TResult> customBotRequired
        )
        {
            return customBotRequired(this);
        }
    }
}
