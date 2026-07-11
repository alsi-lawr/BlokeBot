using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Polly;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Identifies the named policy contracts consumed by later runtime checkpoints.
/// </summary>
public enum TwitchBotResiliencePipeline
{
    IrcSession,
    EventSubSession,
    EventSubChannelRecovery,
    PublicChatDelivery,
}

/// <summary>
/// Mutable host-boundary transport for all required Twitch bot policies.
/// </summary>
public sealed record TwitchBotPolicyOptions
{
    public required IrcSessionResilienceOptions IrcSession { get; init; }

    public required EventSubSessionResilienceOptions EventSubSession { get; init; }

    public required EventSubChannelRecoveryOptions EventSubChannelRecovery { get; init; }

    public required PublicChatRetryOptions PublicChatRetry { get; init; }

    public required PublicChatTerminalRetentionOptions PublicChatTerminalRetention { get; init; }
}

public sealed record IrcSessionResiliencePolicy
{
    public required int AttemptLimit { get; init; }

    public required TimeSpan Delay { get; init; }

    public required TimeSpan MaximumDelay { get; init; }

    public required DelayBackoffType DelayBackoffType { get; init; }

    public required TimeSpan AttemptTimeout { get; init; }
}

public sealed record EventSubSessionResiliencePolicy
{
    public required int AttemptLimit { get; init; }

    public required TimeSpan Delay { get; init; }

    public required TimeSpan MaximumDelay { get; init; }

    public required DelayBackoffType DelayBackoffType { get; init; }

    public required TimeSpan AttemptTimeout { get; init; }
}

public sealed record EventSubChannelRecoveryPolicy
{
    public required int AttemptLimit { get; init; }

    public required TimeSpan Delay { get; init; }

    public required TimeSpan MaximumDelay { get; init; }

    public required DelayBackoffType DelayBackoffType { get; init; }

    public required TimeSpan AttemptTimeout { get; init; }
}

public sealed record PublicChatRetryPolicy
{
    public required int AttemptLimit { get; init; }

    public required TimeSpan Delay { get; init; }

    public required TimeSpan MaximumDelay { get; init; }

    public required DelayBackoffType DelayBackoffType { get; init; }
}

public sealed record PublicChatTerminalRetentionPolicy
{
    public required TimeSpan Duration { get; init; }
}

public sealed record TwitchBotPolicies
{
    public required IrcSessionResiliencePolicy IrcSession { get; init; }

    public required EventSubSessionResiliencePolicy EventSubSession { get; init; }

    public required EventSubChannelRecoveryPolicy EventSubChannelRecovery { get; init; }

    public required PublicChatRetryPolicy PublicChatRetry { get; init; }

    public required PublicChatTerminalRetentionPolicy PublicChatTerminalRetention { get; init; }

    public static TwitchBotPolicies BindRequired(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var policies = configuration.GetSection("Policies");
        return FromOptions(
            new TwitchBotPolicyOptions
            {
                IrcSession = BindRequired<IrcSessionResilienceOptions>(
                    policies.GetSection(nameof(IrcSession))
                ),
                EventSubSession = BindRequired<EventSubSessionResilienceOptions>(
                    policies.GetSection(nameof(EventSubSession))
                ),
                EventSubChannelRecovery = BindRequired<EventSubChannelRecoveryOptions>(
                    policies.GetSection(nameof(EventSubChannelRecovery))
                ),
                PublicChatRetry = BindRequired<PublicChatRetryOptions>(
                    policies.GetSection(nameof(PublicChatRetry))
                ),
                PublicChatTerminalRetention = BindRequired<PublicChatTerminalRetentionOptions>(
                    policies.GetSection(nameof(PublicChatTerminalRetention))
                ),
            },
            policies.Path
        );
    }

    public static TwitchBotPolicies FromOptions(
        TwitchBotPolicyOptions options,
        string boundary = "TwitchBot.Policies"
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);

        var irc = Validate(
            $"{boundary}.{nameof(options.IrcSession)}",
            options.IrcSession,
            new IrcSessionResilienceOptionsValidator()
        );
        var eventSub = Validate(
            $"{boundary}.{nameof(options.EventSubSession)}",
            options.EventSubSession,
            new EventSubSessionResilienceOptionsValidator()
        );
        var recovery = Validate(
            $"{boundary}.{nameof(options.EventSubChannelRecovery)}",
            options.EventSubChannelRecovery,
            new EventSubChannelRecoveryOptionsValidator()
        );
        var publicChat = Validate(
            $"{boundary}.{nameof(options.PublicChatRetry)}",
            options.PublicChatRetry,
            new PublicChatRetryOptionsValidator()
        );
        var retention = Validate(
            $"{boundary}.{nameof(options.PublicChatTerminalRetention)}",
            options.PublicChatTerminalRetention,
            new PublicChatTerminalRetentionOptionsValidator()
        );

        return new TwitchBotPolicies
        {
            IrcSession = new IrcSessionResiliencePolicy
            {
                AttemptLimit = irc.AttemptLimit!.Value,
                Delay = irc.Delay!.Value,
                MaximumDelay = irc.MaximumDelay!.Value,
                DelayBackoffType = irc.DelayBackoffType!.Value,
                AttemptTimeout = irc.AttemptTimeout!.Value,
            },
            EventSubSession = new EventSubSessionResiliencePolicy
            {
                AttemptLimit = eventSub.AttemptLimit!.Value,
                Delay = eventSub.Delay!.Value,
                MaximumDelay = eventSub.MaximumDelay!.Value,
                DelayBackoffType = eventSub.DelayBackoffType!.Value,
                AttemptTimeout = eventSub.AttemptTimeout!.Value,
            },
            EventSubChannelRecovery = new EventSubChannelRecoveryPolicy
            {
                AttemptLimit = recovery.AttemptLimit!.Value,
                Delay = recovery.Delay!.Value,
                MaximumDelay = recovery.MaximumDelay!.Value,
                DelayBackoffType = recovery.DelayBackoffType!.Value,
                AttemptTimeout = recovery.AttemptTimeout!.Value,
            },
            PublicChatRetry = new PublicChatRetryPolicy
            {
                AttemptLimit = publicChat.AttemptLimit!.Value,
                Delay = publicChat.Delay!.Value,
                MaximumDelay = publicChat.MaximumDelay!.Value,
                DelayBackoffType = publicChat.DelayBackoffType!.Value,
            },
            PublicChatTerminalRetention = new PublicChatTerminalRetentionPolicy
            {
                Duration = retention.Duration!.Value,
            },
        };
    }

    private static TOptions BindRequired<TOptions>(IConfigurationSection section)
        where TOptions : class
    {
        if (!section.Exists())
        {
            throw new OptionsValidationException(
                section.Path,
                typeof(TOptions),
                [$"Configuration section '{section.Path}' is required."]
            );
        }

        return section.Get<TOptions>()
            ?? throw new OptionsValidationException(
                section.Path,
                typeof(TOptions),
                [$"Configuration section '{section.Path}' is invalid."]
            );
    }

    private static TOptions Validate<TOptions>(
        string boundary,
        TOptions options,
        IValidateOptions<TOptions> generatedValidator
    )
        where TOptions : class
    {
        var failures = new List<string>();
        AddFailures(generatedValidator.Validate(boundary, options), failures);
        AddFailures(RetryDelayRangeValidator.Validate(options), failures);

        if (failures.Count > 0)
            throw new OptionsValidationException(boundary, typeof(TOptions), failures);

        return options;
    }

    private static void AddFailures(ValidateOptionsResult result, List<string> failures)
    {
        if (result.Failed)
            failures.AddRange(result.Failures);
    }
}

internal static class RetryDelayRangeValidator
{
    internal static ValidateOptionsResult Validate<TOptions>(TOptions options) =>
        options switch
        {
            IrcSessionResilienceOptions value => Validate(value.Delay, value.MaximumDelay),
            EventSubSessionResilienceOptions value => Validate(value.Delay, value.MaximumDelay),
            EventSubChannelRecoveryOptions value => Validate(value.Delay, value.MaximumDelay),
            PublicChatRetryOptions value => Validate(value.Delay, value.MaximumDelay),
            _ => ValidateOptionsResult.Success,
        };

    private static ValidateOptionsResult Validate(TimeSpan? delay, TimeSpan? maximumDelay) =>
        delay is { } minimum && maximumDelay is { } maximum && maximum < minimum
            ? ValidateOptionsResult.Fail("MaximumDelay must be greater than or equal to Delay.")
            : ValidateOptionsResult.Success;
}
