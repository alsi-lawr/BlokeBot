using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Polly;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Mutable configuration transport for the IRC session resilience boundary.
/// </summary>
public sealed record IrcSessionResilienceOptions
{
    [Required]
    [Range(1, int.MaxValue)]
    public required int? AttemptLimit { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? Delay { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? MaximumDelay { get; set; }

    [Required]
    [EnumDataType(typeof(DelayBackoffType))]
    public required DelayBackoffType? DelayBackoffType { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? AttemptTimeout { get; set; }
}

/// <summary>
/// Mutable configuration transport for the EventSub session resilience boundary.
/// </summary>
public sealed record EventSubSessionResilienceOptions
{
    [Required]
    [Range(1, int.MaxValue)]
    public required int? AttemptLimit { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? Delay { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? MaximumDelay { get; set; }

    [Required]
    [EnumDataType(typeof(DelayBackoffType))]
    public required DelayBackoffType? DelayBackoffType { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? AttemptTimeout { get; set; }
}

/// <summary>
/// Mutable configuration transport for per-channel EventSub recovery.
/// </summary>
public sealed record EventSubChannelRecoveryOptions
{
    [Required]
    [Range(1, int.MaxValue)]
    public required int? AttemptLimit { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? Delay { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? MaximumDelay { get; set; }

    [Required]
    [EnumDataType(typeof(DelayBackoffType))]
    public required DelayBackoffType? DelayBackoffType { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? AttemptTimeout { get; set; }
}

/// <summary>
/// Mutable configuration transport for safe public-chat delivery retry.
/// </summary>
public sealed record PublicChatRetryOptions
{
    [Required]
    [Range(1, int.MaxValue)]
    public required int? AttemptLimit { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? Delay { get; set; }

    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? MaximumDelay { get; set; }

    [Required]
    [EnumDataType(typeof(DelayBackoffType))]
    public required DelayBackoffType? DelayBackoffType { get; set; }
}

/// <summary>
/// Mutable configuration transport for the public-chat delivery lifetime.
/// </summary>
public sealed record PublicChatDeliveryLifetimeOptions
{
    [Required]
    [Range(
        typeof(TimeSpan),
        PolicyDuration.Minimum,
        PolicyDuration.MaximumPublicChatAge
    )]
    public required TimeSpan? MaximumAge { get; set; }
}

/// <summary>
/// Mutable configuration transport for redacted public-chat terminal-record retention.
/// </summary>
public sealed record PublicChatTerminalRetentionOptions
{
    [Required]
    [Range(typeof(TimeSpan), PolicyDuration.Minimum, PolicyDuration.Maximum)]
    public required TimeSpan? Duration { get; set; }
}

[OptionsValidator]
public sealed partial class IrcSessionResilienceOptionsValidator
    : IValidateOptions<IrcSessionResilienceOptions>
{
}

[OptionsValidator]
public sealed partial class EventSubSessionResilienceOptionsValidator
    : IValidateOptions<EventSubSessionResilienceOptions>
{
}

[OptionsValidator]
public sealed partial class EventSubChannelRecoveryOptionsValidator
    : IValidateOptions<EventSubChannelRecoveryOptions>
{
}

[OptionsValidator]
public sealed partial class PublicChatRetryOptionsValidator
    : IValidateOptions<PublicChatRetryOptions>
{
}

[OptionsValidator]
public sealed partial class PublicChatDeliveryLifetimeOptionsValidator
    : IValidateOptions<PublicChatDeliveryLifetimeOptions>
{
}

[OptionsValidator]
public sealed partial class PublicChatTerminalRetentionOptionsValidator
    : IValidateOptions<PublicChatTerminalRetentionOptions>
{
}

internal static class PolicyDuration
{
    internal const string Minimum = "00:00:00.0000001";
    internal const string MaximumPublicChatAge = "00:01:00";
    internal const string Maximum = "10675199.02:48:05.4775807";
}
