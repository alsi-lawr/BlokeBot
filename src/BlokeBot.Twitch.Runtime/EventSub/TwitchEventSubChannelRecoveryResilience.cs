using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text.Json;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace BlokeBot.Twitch.Runtime;

internal static class TwitchEventSubChannelRecoveryResilience
{
    internal static void Configure(
        ResiliencePipelineBuilder builder,
        EventSubChannelRecoveryPolicy policy
    )
    {
        if (policy.AttemptLimit > 1)
        {
            builder.AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = policy.AttemptLimit - 1,
                    Delay = policy.Delay,
                    MaxDelay = policy.MaximumDelay,
                    BackoffType = policy.DelayBackoffType,
                    ShouldHandle = args =>
                        ValueTask.FromResult(
                            args.Outcome.Exception is { } exception
                                && TwitchEventSubChannelFailureClassifier.IsRecoverable(
                                    TwitchEventSubChannelFailureClassifier
                                        .Classify(
                                            exception,
                                            TwitchEventSubChannelPhase.Reconciliation,
                                            args.Context.CancellationToken
                                        )
                                        .Classification
                                )
                        ),
                }
            );
        }

        ConfigureAttempt(builder, policy);
    }

    internal static void ConfigureAttempt(
        ResiliencePipelineBuilder builder,
        EventSubChannelRecoveryPolicy policy
    )
    {
        builder.AddTimeout(policy.AttemptTimeout);
    }
}

internal static class TwitchEventSubChannelFailureClassifier
{
    internal static TwitchEventSubChannelFailureDetails Classify(
        Exception exception,
        TwitchEventSubChannelPhase fallbackPhase,
        CancellationToken cancellationToken
    )
    {
        if (exception is TwitchEventSubSubscriptionDeletionUnresolvedException deletion)
        {
            return deletion.Failure;
        }

        var (phase, failure) = exception switch
        {
            TwitchEventSubChannelOperationException operation => (
                operation.Phase,
                operation.Failure
            ),
            _ => (fallbackPhase, exception),
        };
        var classification = failure switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested =>
                TwitchEventSubChannelFailureClassification.Cancellation,
            OperationCanceledException => TwitchEventSubChannelFailureClassification.Timeout,
            TimeoutRejectedException or TimeoutException =>
                TwitchEventSubChannelFailureClassification.Timeout,
            HttpRequestException http when IsTransientHttpStatus(http.StatusCode) =>
                TwitchEventSubChannelFailureClassification.Transient,
            HttpRequestException => TwitchEventSubChannelFailureClassification.Terminal,
            SocketException or WebSocketException or IOException =>
                TwitchEventSubChannelFailureClassification.Transient,
            TwitchAccessTokenUnavailableException
            or AuthenticationException
            or InvalidDataException
            or InvalidOperationException
            or JsonException => TwitchEventSubChannelFailureClassification.Terminal,
            _ => TwitchEventSubChannelFailureClassification.Unexpected,
        };

        return new TwitchEventSubChannelFailureDetails(
            phase,
            classification,
            failure.GetType().FullName ?? failure.GetType().Name,
            failure
        );
    }

    internal static bool IsRecoverable(TwitchEventSubChannelFailureClassification classification)
    {
        return classification
            is TwitchEventSubChannelFailureClassification.Timeout
                or TwitchEventSubChannelFailureClassification.Transient;
    }

    private static bool IsTransientHttpStatus(System.Net.HttpStatusCode? statusCode)
    {
        return TwitchRuntimeSessionFailureClassifier.IsTransientHttpStatus(statusCode);
    }
}

internal readonly record struct TwitchEventSubChannelFailureDetails(
    TwitchEventSubChannelPhase Phase,
    TwitchEventSubChannelFailureClassification Classification,
    string FailureType,
    Exception Exception
)
{
    internal TwitchEventSubChannelFailure ToPublicFailure()
    {
        return new() { Classification = Classification, FailureType = FailureType };
    }
}

internal sealed class TwitchEventSubChannelOperationException(
    TwitchEventSubChannelPhase phase,
    Exception innerException
) : Exception("EventSub channel operation failed.", innerException)
{
    internal TwitchEventSubChannelPhase Phase { get; } = phase;

    internal Exception Failure { get; } = innerException;
}

internal sealed class TwitchEventSubChannelRecoveryPipeline(
    ResiliencePipeline attemptPipeline,
    ResiliencePipeline recoveryPipeline
)
{
    internal ValueTask ExecuteAttemptAsync(
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken
    )
    {
        return attemptPipeline.ExecuteAsync(operation, cancellationToken);
    }

    internal ValueTask ExecuteRecoveryAsync(
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken
    )
    {
        return recoveryPipeline.ExecuteAsync(operation, cancellationToken);
    }
}
