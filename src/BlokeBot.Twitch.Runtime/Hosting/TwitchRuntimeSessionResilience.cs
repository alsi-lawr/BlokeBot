using System.Diagnostics;
using Polly;
using Polly.Retry;

namespace BlokeBot.Twitch.Runtime;

internal static class TwitchRuntimeSessionResilience
{
    internal static void ConfigureIrc(
        ResiliencePipelineBuilder builder,
        IrcSessionResiliencePolicy policy,
        ITwitchRuntimeSessionHealthReporter health
    )
    {
        Configure(
            builder,
            TwitchBotRuntime.Irc,
            policy.AttemptLimit,
            policy.Delay,
            policy.MaximumDelay,
            policy.DelayBackoffType,
            policy.AttemptTimeout,
            TwitchIrcSessionFailureClassifier.Classify,
            health
        );
    }

    internal static void ConfigureEventSub(
        ResiliencePipelineBuilder builder,
        EventSubSessionResiliencePolicy policy,
        ITwitchRuntimeSessionHealthReporter health
    )
    {
        Configure(
            builder,
            TwitchBotRuntime.EventSub,
            policy.AttemptLimit,
            policy.Delay,
            policy.MaximumDelay,
            policy.DelayBackoffType,
            policy.AttemptTimeout,
            EventSubSessionFailureClassifier.Classify,
            health
        );
    }

    private static void Configure(
        ResiliencePipelineBuilder builder,
        TwitchBotRuntime runtime,
        int attemptLimit,
        TimeSpan delay,
        TimeSpan maximumDelay,
        DelayBackoffType delayBackoffType,
        TimeSpan attemptTimeout,
        Func<Exception, CancellationToken, TwitchRuntimeSessionFailureClassification> classify,
        ITwitchRuntimeSessionHealthReporter health
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
                                && TwitchRuntimeSessionFailureClassifier.IsRetryable(
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
                            new TwitchRuntimeSessionHealthReport.RetryScheduled
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

internal sealed class TwitchIrcSessionResiliencePipeline(ResiliencePipeline pipeline)
{
    internal ValueTask<TwitchRuntimeSessionEstablishment> ExecuteAsync(
        Func<CancellationToken, Task<TwitchRuntimeSessionEstablishment>> operation,
        CancellationToken cancellationToken
    )
    {
        return pipeline.ExecuteAsync(
            static (callback, token) =>
                new ValueTask<TwitchRuntimeSessionEstablishment>(callback(token)),
            operation,
            cancellationToken
        );
    }
}

internal sealed class EventSubSessionResiliencePipeline(ResiliencePipeline pipeline)
{
    internal ValueTask<TwitchRuntimeSessionEstablishment> ExecuteAsync(
        Func<CancellationToken, Task<TwitchRuntimeSessionEstablishment>> operation,
        CancellationToken cancellationToken
    )
    {
        return pipeline.ExecuteAsync(
            static (callback, token) =>
                new ValueTask<TwitchRuntimeSessionEstablishment>(callback(token)),
            operation,
            cancellationToken
        );
    }
}
