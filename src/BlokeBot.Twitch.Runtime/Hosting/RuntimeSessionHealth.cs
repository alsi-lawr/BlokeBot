using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal enum RuntimeSessionFailureClassification
{
    Transient,
    Terminal,
    Unexpected,
    Timeout,
    Cancellation,
}

internal abstract record RuntimeSessionHealthReport
{
    private RuntimeSessionHealthReport() { }

    internal required ChatRuntime Runtime { get; init; }

    internal required RuntimeSessionFailureClassification Classification { get; init; }

    internal required int Attempt { get; init; }

    internal required Exception Exception { get; init; }

    internal Type FailureType => Exception.GetType();

    internal sealed record RetryScheduled : RuntimeSessionHealthReport;

    internal sealed record ReconnectScheduled : RuntimeSessionHealthReport;

    internal sealed record Unhealthy : RuntimeSessionHealthReport;
}

internal interface IRuntimeSessionHealthReporter
{
    void Report(RuntimeSessionHealthReport report);
}

internal sealed class RuntimeSessionHealthLogger(ILogger<RuntimeSessionHealthLogger> log)
    : IRuntimeSessionHealthReporter
{
    public void Report(RuntimeSessionHealthReport report)
    {
        switch (report)
        {
            case RuntimeSessionHealthReport.RetryScheduled retry:
                log.LogWarning(
                    "{Runtime} session attempt {Attempt} failed with {Classification} ({FailureType}); a bounded retry is scheduled.",
                    retry.Runtime,
                    retry.Attempt,
                    retry.Classification,
                    retry.FailureType.FullName
                );
                return;
            case RuntimeSessionHealthReport.ReconnectScheduled reconnect:
                log.LogWarning(
                    "{Runtime} session established on attempt {Attempt} disconnected with {Classification} ({FailureType}); a fresh bounded establishment cycle is scheduled.",
                    reconnect.Runtime,
                    reconnect.Attempt,
                    reconnect.Classification,
                    reconnect.FailureType.FullName
                );
                return;
            case RuntimeSessionHealthReport.Unhealthy unhealthy:
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
