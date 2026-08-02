using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text.Json;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace BlokeBot.Twitch.Runtime;

internal static class EventSubChannelRecoveryResilience
{
    internal static void Configure(
        ResiliencePipelineBuilder<EventSubChannelReconciliationOutcome> builder,
        EventSubChannelRecoveryPolicy policy
    )
    {
        if (policy.AttemptLimit > 1)
        {
            builder.AddRetry(
                new RetryStrategyOptions<EventSubChannelReconciliationOutcome>
                {
                    MaxRetryAttempts = policy.AttemptLimit - 1,
                    Delay = policy.Delay,
                    MaxDelay = policy.MaximumDelay,
                    BackoffType = policy.DelayBackoffType,
                    ShouldHandle = args =>
                        ValueTask.FromResult(
                            args.Outcome.Exception is { } exception
                                ? EventSubChannelFailureClassifier.IsRecoverable(
                                    EventSubChannelFailureClassifier
                                        .Classify(
                                            exception,
                                            EventSubChannelPhase.Reconciliation,
                                            args.Context.CancellationToken
                                        )
                                        .Classification
                                )
                                : args.Outcome.Result
                                    is EventSubChannelReconciliationOutcome.UnresolvedDeletion unresolved
                                    && EventSubChannelFailureClassifier.IsRecoverable(
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
    ) => builder.AddTimeout(policy.AttemptTimeout);
}

internal static class EventSubChannelFailureClassifier
{
    internal static EventSubChannelFailureDetails Classify(
        Exception exception,
        EventSubChannelPhase fallbackPhase,
        CancellationToken cancellationToken
    )
    {
        var (phase, failure) = exception switch
        {
            EventSubChannelOperationException operation => (operation.Phase, operation.Failure),
            _ => (fallbackPhase, exception),
        };
        var classification = failure switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested =>
                EventSubChannelFailureClassification.Cancellation,
            OperationCanceledException => EventSubChannelFailureClassification.Timeout,
            TimeoutRejectedException or TimeoutException =>
                EventSubChannelFailureClassification.Timeout,
            HttpRequestException http when IsTransientHttpStatus(http.StatusCode) =>
                EventSubChannelFailureClassification.Transient,
            HttpRequestException => EventSubChannelFailureClassification.Terminal,
            SocketException or WebSocketException or IOException =>
                EventSubChannelFailureClassification.Transient,
            AuthenticationException
            or InvalidDataException
            or InvalidOperationException
            or JsonException => EventSubChannelFailureClassification.Terminal,
            _ => EventSubChannelFailureClassification.Unexpected,
        };

        return new EventSubChannelFailureDetails(
            phase,
            classification,
            failure.GetType().FullName ?? failure.GetType().Name,
            failure
        );
    }

    internal static bool IsRecoverable(EventSubChannelFailureClassification classification) =>
        classification
            is EventSubChannelFailureClassification.Timeout
                or EventSubChannelFailureClassification.Transient;

    private static bool IsTransientHttpStatus(System.Net.HttpStatusCode? statusCode) =>
        RuntimeSessionFailureClassifier.IsTransientHttpStatus(statusCode);
}

internal readonly record struct EventSubChannelFailureDetails(
    EventSubChannelPhase Phase,
    EventSubChannelFailureClassification Classification,
    string FailureType,
    Exception Exception
)
{
    internal EventSubChannelFailure ToPublicFailure() =>
        new() { Classification = Classification, FailureType = FailureType };
}

internal abstract record EventSubChannelFailureContext
{
    private EventSubChannelFailureContext() { }

    internal abstract EventSubChannelPhase Phase { get; }

    internal abstract EventSubChannelFailureClassification Classification { get; }

    internal abstract string FailureType { get; }

    internal abstract TResult Match<TResult>(
        Func<ClassifiedException, TResult> classifiedException,
        Func<MissingChannel, TResult> missingChannel,
        Func<MissingBot, TResult> missingBot,
        Func<StartupMessageRejected, TResult> startupMessageRejected,
        Func<TokenUnavailable, TResult> tokenUnavailable
    );

    internal EventSubChannelFailure ToPublicFailure() =>
        new() { Classification = Classification, FailureType = FailureType };

    internal sealed record ClassifiedException(EventSubChannelFailureDetails Details)
        : EventSubChannelFailureContext
    {
        internal override EventSubChannelPhase Phase => Details.Phase;

        internal override EventSubChannelFailureClassification Classification =>
            Details.Classification;

        internal override string FailureType => Details.FailureType;

        internal override TResult Match<TResult>(
            Func<ClassifiedException, TResult> classifiedException,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<StartupMessageRejected, TResult> startupMessageRejected,
            Func<TokenUnavailable, TResult> tokenUnavailable
        ) => classifiedException(this);

        public override string ToString() =>
            $"{nameof(ClassifiedException)} {{ Phase = {Phase}, Classification = {Classification}, FailureType = {FailureType} }}";
    }

    internal sealed record MissingChannel : EventSubChannelFailureContext
    {
        internal override EventSubChannelPhase Phase => EventSubChannelPhase.SubscriptionSetup;

        internal override EventSubChannelFailureClassification Classification =>
            EventSubChannelFailureClassification.Terminal;

        internal override string FailureType => "MissingChannel";

        internal override TResult Match<TResult>(
            Func<ClassifiedException, TResult> classifiedException,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<StartupMessageRejected, TResult> startupMessageRejected,
            Func<TokenUnavailable, TResult> tokenUnavailable
        ) => missingChannel(this);
    }

    internal sealed record MissingBot : EventSubChannelFailureContext
    {
        internal override EventSubChannelPhase Phase => EventSubChannelPhase.SubscriptionSetup;

        internal override EventSubChannelFailureClassification Classification =>
            EventSubChannelFailureClassification.Terminal;

        internal override string FailureType => "MissingBot";

        internal override TResult Match<TResult>(
            Func<ClassifiedException, TResult> classifiedException,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<StartupMessageRejected, TResult> startupMessageRejected,
            Func<TokenUnavailable, TResult> tokenUnavailable
        ) => missingBot(this);
    }

    internal sealed record TokenUnavailable(AccessTokenUnavailableReason Reason)
        : EventSubChannelFailureContext
    {
        internal override EventSubChannelPhase Phase => EventSubChannelPhase.AccountResolution;

        internal override EventSubChannelFailureClassification Classification =>
            EventSubChannelFailureClassification.Terminal;

        internal override string FailureType => Reason.ToString();

        internal override TResult Match<TResult>(
            Func<ClassifiedException, TResult> classifiedException,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<StartupMessageRejected, TResult> startupMessageRejected,
            Func<TokenUnavailable, TResult> tokenUnavailable
        ) => tokenUnavailable(this);
    }

    internal sealed record StartupMessageRejected : EventSubChannelFailureContext
    {
        internal override EventSubChannelPhase Phase => EventSubChannelPhase.SubscriptionSetup;

        internal override EventSubChannelFailureClassification Classification =>
            EventSubChannelFailureClassification.Terminal;

        internal override string FailureType => "PublicChatEnqueueRejected";

        internal override TResult Match<TResult>(
            Func<ClassifiedException, TResult> classifiedException,
            Func<MissingChannel, TResult> missingChannel,
            Func<MissingBot, TResult> missingBot,
            Func<StartupMessageRejected, TResult> startupMessageRejected,
            Func<TokenUnavailable, TResult> tokenUnavailable
        ) => startupMessageRejected(this);
    }
}

internal sealed class EventSubChannelOperationException(
    EventSubChannelPhase phase,
    Exception innerException
) : Exception("EventSub channel operation failed.", innerException)
{
    internal EventSubChannelPhase Phase { get; } = phase;

    internal Exception Failure { get; } = innerException;
}

internal sealed class EventSubChannelRecoveryPipeline(
    ResiliencePipeline attemptPipeline,
    ResiliencePipeline<EventSubChannelReconciliationOutcome> recoveryPipeline
)
{
    internal ValueTask<TResult> ExecuteAttemptAsync<TResult>(
        Func<CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken
    ) => attemptPipeline.ExecuteAsync(operation, cancellationToken);

    internal ValueTask<EventSubChannelReconciliationOutcome> ExecuteRecoveryAsync(
        Func<CancellationToken, ValueTask<EventSubChannelReconciliationOutcome>> operation,
        CancellationToken cancellationToken
    ) => recoveryPipeline.ExecuteAsync(operation, cancellationToken);
}
