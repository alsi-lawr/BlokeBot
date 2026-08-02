using System.Diagnostics;
using Polly;
using Polly.Retry;

namespace BlokeBot.Twitch.Runtime;

internal static class RuntimeSessionResilience
{
    internal static void ConfigureIrc(
        ResiliencePipelineBuilder builder,
        IrcSessionResiliencePolicy policy,
        IRuntimeSessionHealthReporter health
    ) =>
        Configure(
            builder,
            ChatRuntime.Irc,
            policy.AttemptLimit,
            policy.Delay,
            policy.MaximumDelay,
            policy.DelayBackoffType,
            policy.AttemptTimeout,
            IrcSessionFailureClassifier.Classify,
            health
        );

    internal static void ConfigureEventSub(
        ResiliencePipelineBuilder builder,
        EventSubSessionResiliencePolicy policy,
        IRuntimeSessionHealthReporter health
    ) =>
        Configure(
            builder,
            ChatRuntime.EventSub,
            policy.AttemptLimit,
            policy.Delay,
            policy.MaximumDelay,
            policy.DelayBackoffType,
            policy.AttemptTimeout,
            EventSubSessionFailureClassifier.Classify,
            health
        );

    private static void Configure(
        ResiliencePipelineBuilder builder,
        ChatRuntime runtime,
        int attemptLimit,
        TimeSpan delay,
        TimeSpan maximumDelay,
        DelayBackoffType delayBackoffType,
        TimeSpan attemptTimeout,
        Func<Exception, CancellationToken, RuntimeSessionFailureClassification> classify,
        IRuntimeSessionHealthReporter health
    )
    {
        if (attemptLimit > 1)
        {
            builder.AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = attemptLimit - 1,
                    Delay = delay,
                    MaxDelay = maximumDelay,
                    BackoffType = delayBackoffType,
                    ShouldHandle = args =>
                        ValueTask.FromResult(
                            args.Outcome.Exception is { } exception
                                && RuntimeSessionFailureClassifier.IsRetryable(
                                    classify(exception, args.Context.CancellationToken)
                                )
                        ),
                    OnRetry = args =>
                    {
                        var exception =
                            args.Outcome.Exception
                            ?? throw new UnreachableException(
                                "A session retry requires an exception outcome."
                            );
                        health.Report(
                            new RuntimeSessionHealthReport.RetryScheduled
                            {
                                Runtime = runtime,
                                Classification = classify(
                                    exception,
                                    args.Context.CancellationToken
                                ),
                                Attempt = args.AttemptNumber + 1,
                                Exception = exception,
                            }
                        );
                        return ValueTask.CompletedTask;
                    },
                }
            );
        }

        builder.AddTimeout(attemptTimeout);
    }
}

internal sealed class IrcSessionResiliencePipeline(ResiliencePipeline pipeline)
{
    internal ValueTask<RuntimeSessionEstablishment> ExecuteAsync(
        Func<CancellationToken, Task<RuntimeSessionEstablishment>> operation,
        CancellationToken cancellationToken
    ) =>
        pipeline.ExecuteAsync(
            static (callback, token) => new ValueTask<RuntimeSessionEstablishment>(callback(token)),
            operation,
            cancellationToken
        );
}

internal sealed class EventSubSessionResiliencePipeline(ResiliencePipeline pipeline)
{
    internal ValueTask<RuntimeSessionEstablishment> ExecuteAsync(
        Func<CancellationToken, Task<RuntimeSessionEstablishment>> operation,
        CancellationToken cancellationToken
    ) =>
        pipeline.ExecuteAsync(
            static (callback, token) => new ValueTask<RuntimeSessionEstablishment>(callback(token)),
            operation,
            cancellationToken
        );
}
