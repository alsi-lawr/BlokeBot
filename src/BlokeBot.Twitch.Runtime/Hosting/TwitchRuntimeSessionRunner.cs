using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal static class TwitchRuntimeSessionRunner
{
    internal static async Task RunUntilStoppedAsync(
        Func<CancellationToken, Task<TwitchRuntimeSessionOutcome>> runSession,
        ITwitchRuntimeIdleWait idleWait,
        CancellationToken stoppingToken
    )
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var outcome = await runSession(stoppingToken);
            switch (outcome)
            {
                case TwitchRuntimeSessionOutcome.Completed:
                    try
                    {
                        await idleWait.WaitAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (
                        stoppingToken.IsCancellationRequested
                    )
                    {
                        return;
                    }
                    break;
                case TwitchRuntimeSessionOutcome.Canceled:
                case TwitchRuntimeSessionOutcome.Unhealthy:
                    return;
                default:
                    throw new UnreachableException("Unknown runtime session outcome.");
            }
        }
    }

    internal static async Task<TwitchRuntimeSessionOutcome> RunOnceAsync(
        TwitchBotRuntime runtime,
        Func<CancellationToken, Task> runSession,
        Func<Func<CancellationToken, Task>, CancellationToken, ValueTask> execute,
        Func<
            Exception,
            CancellationToken,
            TwitchRuntimeSessionFailureClassification
        > classify,
        ITwitchRuntimeSessionHealthReporter health,
        TwitchBotRuntimeStatusStore status,
        CancellationToken stoppingToken
    )
    {
        var attempt = 0;
        try
        {
            await execute(RunAttemptAsync, stoppingToken);
            return stoppingToken.IsCancellationRequested
                ? new TwitchRuntimeSessionOutcome.Canceled()
                : new TwitchRuntimeSessionOutcome.Completed();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return new TwitchRuntimeSessionOutcome.Canceled();
        }
        catch (Exception exception)
        {
            if (exception is TwitchAccessTokenUnavailableException)
                status.SetAuthorized(false);

            var report = new TwitchRuntimeSessionHealthReport.Unhealthy
            {
                Runtime = runtime,
                Classification = classify(exception, stoppingToken),
                Attempt = attempt,
                Exception = exception,
            };
            health.Report(report);
            return new TwitchRuntimeSessionOutcome.Unhealthy { Report = report };
        }

        async Task RunAttemptAsync(CancellationToken attemptToken)
        {
            checked
            {
                attempt++;
            }

            try
            {
                await runSession(attemptToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                status.SetConnected(false, []);
                throw;
            }
        }
    }
}

internal abstract record TwitchRuntimeSessionOutcome
{
    private protected TwitchRuntimeSessionOutcome() { }

    private protected abstract void Seal();

    internal sealed record Completed : TwitchRuntimeSessionOutcome
    {
        private protected override void Seal() { }
    }

    internal sealed record Canceled : TwitchRuntimeSessionOutcome
    {
        private protected override void Seal() { }
    }

    internal sealed record Unhealthy : TwitchRuntimeSessionOutcome
    {
        internal required TwitchRuntimeSessionHealthReport.Unhealthy Report { get; init; }

        private protected override void Seal() { }
    }
}

internal interface ITwitchRuntimeIdleWait
{
    ValueTask WaitAsync(CancellationToken cancellationToken);
}

internal sealed class TwitchRuntimeIdleWait(TimeProvider timeProvider) : ITwitchRuntimeIdleWait
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

    public ValueTask WaitAsync(CancellationToken cancellationToken) =>
        new(Task.Delay(IdleInterval, timeProvider, cancellationToken));
}
