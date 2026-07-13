using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal enum TwitchRuntimeSessionFailureClassification
{
    Transient,
    Terminal,
    Unexpected,
    Timeout,
    Cancellation,
}

internal abstract record TwitchRuntimeSessionHealthReport
{
    private TwitchRuntimeSessionHealthReport() { }

    internal required TwitchBotRuntime Runtime { get; init; }

    internal required TwitchRuntimeSessionFailureClassification Classification { get; init; }

    internal required int Attempt { get; init; }

    internal required Exception Exception { get; init; }

    internal Type FailureType => Exception.GetType();

    internal sealed record RetryScheduled : TwitchRuntimeSessionHealthReport;

    internal sealed record ReconnectScheduled : TwitchRuntimeSessionHealthReport;

    internal sealed record Unhealthy : TwitchRuntimeSessionHealthReport;
}

internal interface ITwitchRuntimeSessionHealthReporter
{
    void Report(TwitchRuntimeSessionHealthReport report);
}

internal sealed class TwitchRuntimeSessionHealthLogger(
    ILogger<TwitchRuntimeSessionHealthLogger> log
) : ITwitchRuntimeSessionHealthReporter
{
    public void Report(TwitchRuntimeSessionHealthReport report)
    {
        switch (report)
        {
            case TwitchRuntimeSessionHealthReport.RetryScheduled retry:
                log.LogWarning(
                    "{Runtime} session attempt {Attempt} failed with {Classification} ({FailureType}); a bounded retry is scheduled.",
                    retry.Runtime,
                    retry.Attempt,
                    retry.Classification,
                    retry.FailureType.FullName
                );
                return;
            case TwitchRuntimeSessionHealthReport.ReconnectScheduled reconnect:
                log.LogWarning(
                    "{Runtime} session established on attempt {Attempt} disconnected with {Classification} ({FailureType}); a fresh bounded establishment cycle is scheduled.",
                    reconnect.Runtime,
                    reconnect.Attempt,
                    reconnect.Classification,
                    reconnect.FailureType.FullName
                );
                return;
            case TwitchRuntimeSessionHealthReport.Unhealthy unhealthy:
                log.LogError(
                    "{Runtime} session attempt {Attempt} failed with {Classification} ({FailureType}); the runtime session is unhealthy and no further retry is configured.",
                    unhealthy.Runtime,
                    unhealthy.Attempt,
                    unhealthy.Classification,
                    unhealthy.FailureType.FullName
                );
                return;
        }

        throw new UnreachableException("Unknown runtime session health report.");
    }
}
