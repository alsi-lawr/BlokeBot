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
        ResiliencePipelineBuilder<TwitchEventSubChannelReconciliationOutcome> builder,
        EventSubChannelRecoveryPolicy policy
    )
    {
        if (policy.AttemptLimit > 1)
        {
            builder.AddRetry(
                new RetryStrategyOptions<TwitchEventSubChannelReconciliationOutcome>
                {
                    MaxRetryAttempts = policy.AttemptLimit - 1,
                    Delay = policy.Delay,
                    MaxDelay = policy.MaximumDelay,
                    BackoffType = policy.DelayBackoffType,
                    ShouldHandle = args =>
                        ValueTask.FromResult(
                            args.Outcome.Exception is { } exception
                                ? TwitchEventSubChannelFailureClassifier.IsRecoverable(
                                    TwitchEventSubChannelFailureClassifier
                                        .Classify(
                                            exception,
                                            TwitchEventSubChannelPhase.Reconciliation,
                                            args.Context.CancellationToken
                                        )
                                        .Classification
                                )
                                : args.Outcome.Result
                                    is TwitchEventSubChannelReconciliationOutcome.UnresolvedDeletion unresolved
                                    && TwitchEventSubChannelFailureClassifier.IsRecoverable(
                                        unresolved.Failure.Classification
                                    )
                        ),
                }
            );
        }

        builder.AddTimeout(policy.AttemptTimeout);
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

internal abstract record TwitchEventSubChannelFailureContext
{
    private protected TwitchEventSubChannelFailureContext() { }

    internal abstract TwitchEventSubChannelPhase Phase { get; }

    internal abstract TwitchEventSubChannelFailureClassification Classification { get; }

    internal abstract string FailureType { get; }

    internal abstract TResult Match<TResult>(
        Func<ClassifiedException, TResult> classifiedException,
        Func<MissingChannel, TResult> missingChannel,
        Func<MissingBot, TResult> missingBot,
        Func<StartupMessageRejected, TResult> startupMessageRejected
    );

    internal TwitchEventSubChannelFailure ToPublicFailure()
    {
        return new() { Classification = Classification, FailureType = FailureType };
    }

    private protected abstract void Seal();

    internal sealed record ClassifiedException(TwitchEventSubChannelFailureDetails Details)
        : TwitchEventSubChannelFailureContext
    {
        internal override TwitchEventSubChannelPhase Phase => Details.Phase;

        internal override TwitchEventSubChannelFailureClassification Classification =>
            Details.Classification;

        internal override string FailureType => Details.FailureType;

        internal override TResult Match<TResult>(
            Func<ClassifiedException, TResult> classifiedException,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<StartupMessageRejected, TResult> startupMessageRejected
        )
        {
            return classifiedException(this);
        }

        private protected override void Seal() { }

        public override string ToString()
        {
            return $"{nameof(ClassifiedException)} {{ Phase = {Phase}, Classification = {Classification}, FailureType = {FailureType} }}";
        }
    }

    internal sealed record MissingChannel : TwitchEventSubChannelFailureContext
    {
        internal override TwitchEventSubChannelPhase Phase =>
            TwitchEventSubChannelPhase.SubscriptionSetup;

        internal override TwitchEventSubChannelFailureClassification Classification =>
            TwitchEventSubChannelFailureClassification.Terminal;

        internal override string FailureType => "MissingChannel";

        internal override TResult Match<TResult>(
            Func<ClassifiedException, TResult> classifiedException,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<StartupMessageRejected, TResult> startupMessageRejected
        )
        {
            return missingChannel(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record MissingBot : TwitchEventSubChannelFailureContext
    {
        internal override TwitchEventSubChannelPhase Phase =>
            TwitchEventSubChannelPhase.SubscriptionSetup;

        internal override TwitchEventSubChannelFailureClassification Classification =>
            TwitchEventSubChannelFailureClassification.Terminal;

        internal override string FailureType => "MissingBot";

        internal override TResult Match<TResult>(
            Func<ClassifiedException, TResult> classifiedException,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<StartupMessageRejected, TResult> startupMessageRejected
        )
        {
            return missingBot(this);
        }

        private protected override void Seal() { }
    }

    internal sealed record StartupMessageRejected : TwitchEventSubChannelFailureContext
    {
        internal override TwitchEventSubChannelPhase Phase =>
            TwitchEventSubChannelPhase.SubscriptionSetup;

        internal override TwitchEventSubChannelFailureClassification Classification =>
            TwitchEventSubChannelFailureClassification.Terminal;

        internal override string FailureType => "PublicChatEnqueueRejected";

        internal override TResult Match<TResult>(
            Func<ClassifiedException, TResult> classifiedException,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<StartupMessageRejected, TResult> startupMessageRejected
        )
        {
            return startupMessageRejected(this);
        }

        private protected override void Seal() { }
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
    ResiliencePipeline<TwitchEventSubChannelReconciliationOutcome> recoveryPipeline
)
{
    internal ValueTask<TResult> ExecuteAttemptAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        return attemptPipeline.ExecuteAsync(operation, cancellationToken);
    }

    internal ValueTask<TwitchEventSubChannelReconciliationOutcome> ExecuteRecoveryAsync(
        Func<CancellationToken, ValueTask<TwitchEventSubChannelReconciliationOutcome>> operation,
        CancellationToken cancellationToken
    )
    {
        return recoveryPipeline.ExecuteAsync(operation, cancellationToken);
    }
}
