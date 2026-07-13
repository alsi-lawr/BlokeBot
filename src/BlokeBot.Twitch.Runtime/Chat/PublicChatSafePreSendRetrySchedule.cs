using System.Diagnostics;
using Polly;

namespace BlokeBot.Twitch.Runtime;

internal readonly record struct PublicChatSafePreSendFailureCount(int Value)
{
    internal PublicChatSafePreSendFailureCount Next()
    {
        return new(checked(Value + 1));
    }
}

internal abstract record PublicChatSafePreSendRetryDecision
{
    private PublicChatSafePreSendRetryDecision() { }

    internal sealed record Scheduled : PublicChatSafePreSendRetryDecision
    {
        internal required PublicChatSafePreSendFailureCount FailureCount { get; init; }

        internal required DateTimeOffset NextAttemptAtUtc { get; init; }
    }

    internal sealed record Exhausted : PublicChatSafePreSendRetryDecision
    {
        internal required PublicChatSafePreSendFailureCount FailureCount { get; init; }
    }
}

internal static class PublicChatSafePreSendRetrySchedule
{
    internal static PublicChatSafePreSendRetryDecision Create(
        PublicChatRetryPolicy policy,
        PublicChatSafePreSendFailureCount previousFailureCount,
        DateTimeOffset failedAtUtc
    )
    {
        ArgumentNullException.ThrowIfNull(policy);

        var failureCount = previousFailureCount.Next();
        return failureCount.Value >= policy.AttemptLimit
            ? new PublicChatSafePreSendRetryDecision.Exhausted
            {
                FailureCount = failureCount,
            }
            : new PublicChatSafePreSendRetryDecision.Scheduled
            {
                FailureCount = failureCount,
                NextAttemptAtUtc = failedAtUtc.Add(DelayFor(policy, failureCount)),
            };
    }

    private static TimeSpan DelayFor(
        PublicChatRetryPolicy policy,
        PublicChatSafePreSendFailureCount failureCount
    )
    {
        return policy.DelayBackoffType switch
        {
            DelayBackoffType.Constant => policy.Delay,
            DelayBackoffType.Linear => LinearDelay(
                policy.Delay,
                policy.MaximumDelay,
                failureCount.Value
            ),
            DelayBackoffType.Exponential => ExponentialDelay(
                policy.Delay,
                policy.MaximumDelay,
                failureCount.Value
            ),
            _ => throw new UnreachableException(
                $"Unknown public chat retry backoff type {policy.DelayBackoffType}."
            ),
        };
    }

    private static TimeSpan LinearDelay(
        TimeSpan delay,
        TimeSpan maximumDelay,
        int retryNumber
    )
    {
        return delay.Ticks > maximumDelay.Ticks / retryNumber
            ? maximumDelay
            : TimeSpan.FromTicks(delay.Ticks * retryNumber);
    }

    private static TimeSpan ExponentialDelay(
        TimeSpan delay,
        TimeSpan maximumDelay,
        int retryNumber
    )
    {
        var backoff = delay;
        for (var index = 1; index < retryNumber; index++)
        {
            if (backoff.Ticks > maximumDelay.Ticks / 2)
            {
                return maximumDelay;
            }

            backoff = TimeSpan.FromTicks(backoff.Ticks * 2);
        }

        return backoff <= maximumDelay ? backoff : maximumDelay;
    }
}
