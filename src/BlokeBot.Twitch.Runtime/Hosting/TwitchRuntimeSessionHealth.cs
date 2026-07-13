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
    private protected TwitchRuntimeSessionHealthReport() { }

    internal required TwitchBotRuntime Runtime { get; init; }

    internal required TwitchRuntimeSessionFailureClassification Classification { get; init; }

    internal required int Attempt { get; init; }

    internal required Exception Exception { get; init; }

    internal Type FailureType => Exception.GetType();

    internal void Log(ILogger<TwitchRuntimeSessionHealthReport> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        switch (this)
        {
            case RetryScheduled retry:
                log.LogWarning(
                    "{Runtime} session attempt {Attempt} failed with {Classification} ({FailureType}); a bounded retry is scheduled.",
                    retry.Runtime,
                    retry.Attempt,
                    retry.Classification,
                    retry.FailureType.FullName
                );
                return;
            case ReconnectScheduled reconnect:
                log.LogWarning(
                    "{Runtime} session established on attempt {Attempt} disconnected with {Classification} ({FailureType}); a fresh bounded establishment cycle is scheduled.",
                    reconnect.Runtime,
                    reconnect.Attempt,
                    reconnect.Classification,
                    reconnect.FailureType.FullName
                );
                return;
            case Unhealthy unhealthy:
                log.LogError(
                    "{Runtime} session attempt {Attempt} failed with {Classification} ({FailureType}); the runtime session is unhealthy and no further retry is configured.",
                    unhealthy.Runtime,
                    unhealthy.Attempt,
                    unhealthy.Classification,
                    unhealthy.FailureType.FullName
                );
                return;
            default:
                throw new UnreachableException("Unknown runtime session health report.");
        }
    }

    private protected abstract void Seal();

    internal sealed record RetryScheduled : TwitchRuntimeSessionHealthReport
    {
        private protected override void Seal() { }
    }

    internal sealed record ReconnectScheduled : TwitchRuntimeSessionHealthReport
    {
        private protected override void Seal() { }
    }

    internal sealed record Unhealthy : TwitchRuntimeSessionHealthReport
    {
        private protected override void Seal() { }
    }
}
