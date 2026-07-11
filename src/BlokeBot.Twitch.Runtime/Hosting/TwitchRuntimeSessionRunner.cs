using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal static class TwitchRuntimeSessionRunner
{
    internal static async Task RunUntilStoppedAsync(
        TwitchBotRuntime runtime,
        TwitchRuntimeConnectionTarget initialTarget,
        Func<
            TwitchRuntimeConnectionTarget,
            CancellationToken,
            Task<TwitchRuntimeSessionOutcome>
        > establishSession,
        Func<
            Exception,
            CancellationToken,
            TwitchRuntimeSessionFailureClassification
        > classify,
        ITwitchRuntimeSessionHealthReporter health,
        TwitchBotRuntimeStatusStore status,
        ITwitchRuntimeIdleWait idleWait,
        CancellationToken stoppingToken
    )
    {
        var target = initialTarget;
        TwitchRuntimeSessionHandoff handoff = new TwitchRuntimeSessionHandoff.None();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var outcome = await establishSession(target, stoppingToken);
                switch (outcome)
                {
                    case TwitchRuntimeSessionOutcome.Idle:
                        var idleHandoff = handoff;
                        handoff = new TwitchRuntimeSessionHandoff.None();
                        await DisposeHandoffAsync(idleHandoff);
                        if (!await WaitForChannelsAsync(idleWait, stoppingToken))
                            return;
                        break;
                    case TwitchRuntimeSessionOutcome.Established established:
                        var completedHandoff = handoff;
                        handoff = new TwitchRuntimeSessionHandoff.None();
                        await DisposeHandoffAsync(completedHandoff);
                        target = initialTarget;
                        var nextTarget = await ListenAsync(
                            runtime,
                            established,
                            classify,
                            health,
                            status,
                            stoppingToken
                        );
                        switch (nextTarget)
                        {
                            case TwitchRuntimeListenOutcome.Reconnect reconnect:
                                target = reconnect.Target;
                                break;
                            case TwitchRuntimeListenOutcome.ProtocolHandoff protocol:
                                target = protocol.Target;
                                handoff = new TwitchRuntimeSessionHandoff.Pending
                                {
                                    Session = protocol.PreviousSession,
                                };
                                break;
                            case TwitchRuntimeListenOutcome.Canceled:
                            case TwitchRuntimeListenOutcome.Unhealthy:
                                return;
                            default:
                                throw new UnreachableException(
                                    "Unknown runtime listening outcome."
                                );
                        }
                        break;
                    case TwitchRuntimeSessionOutcome.Canceled:
                    case TwitchRuntimeSessionOutcome.Unhealthy:
                        return;
                    default:
                        throw new UnreachableException(
                            "Unknown runtime session establishment outcome."
                        );
                }
            }
        }
        finally
        {
            await DisposeHandoffAsync(handoff);
        }
    }

    internal static async Task<TwitchRuntimeSessionOutcome> EstablishOnceAsync(
        TwitchBotRuntime runtime,
        Func<CancellationToken, Task<TwitchRuntimeSessionEstablishment>> establishSession,
        Func<
            Func<CancellationToken, Task<TwitchRuntimeSessionEstablishment>>,
            CancellationToken,
            ValueTask<TwitchRuntimeSessionEstablishment>
        > execute,
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
            var establishment = await execute(RunAttemptAsync, stoppingToken);
            if (stoppingToken.IsCancellationRequested)
            {
                if (establishment is TwitchRuntimeSessionEstablishment.Established connected)
                    await connected.Session.DisposeAsync();

                return new TwitchRuntimeSessionOutcome.Canceled();
            }

            return establishment switch
            {
                TwitchRuntimeSessionEstablishment.Idle =>
                    new TwitchRuntimeSessionOutcome.Idle(),
                TwitchRuntimeSessionEstablishment.Established established =>
                    new TwitchRuntimeSessionOutcome.Established
                    {
                        Session = established.Session,
                        Attempt = attempt,
                    },
                _ => throw new UnreachableException(
                    "Unknown runtime session establishment."
                ),
            };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return new TwitchRuntimeSessionOutcome.Canceled();
        }
        catch (Exception exception)
        {
            if (exception is TwitchAccessTokenUnavailableException)
                status.SetAuthorized(false);

            var report = CreateUnhealthyReport(
                runtime,
                classify,
                attempt,
                exception,
                stoppingToken
            );
            health.Report(report);
            return new TwitchRuntimeSessionOutcome.Unhealthy
            {
                Report = report,
            };
        }

        async Task<TwitchRuntimeSessionEstablishment> RunAttemptAsync(
            CancellationToken attemptToken
        )
        {
            checked
            {
                attempt++;
            }

            try
            {
                return await establishSession(attemptToken);
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

    private static async Task<TwitchRuntimeListenOutcome> ListenAsync(
        TwitchBotRuntime runtime,
        TwitchRuntimeSessionOutcome.Established established,
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
        var session = established.Session;
        try
        {
            var reconnect = await session.ListenAsync(stoppingToken);
            return new TwitchRuntimeListenOutcome.ProtocolHandoff
            {
                Target = reconnect.Target,
                PreviousSession = session,
            };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await session.DisposeAsync();
            return new TwitchRuntimeListenOutcome.Canceled();
        }
        catch (Exception exception)
        {
            await session.DisposeAsync();
            status.SetConnected(false, []);
            var classification = classify(exception, stoppingToken);
            if (TwitchRuntimeSessionFailureClassifier.IsRetryable(classification))
            {
                health.Report(
                    new TwitchRuntimeSessionHealthReport.ReconnectScheduled
                    {
                        Runtime = runtime,
                        Classification = classification,
                        Attempt = established.Attempt,
                        Exception = exception,
                    }
                );
                return new TwitchRuntimeListenOutcome.Reconnect
                {
                    Target = new TwitchRuntimeConnectionTarget.Initial(),
                };
            }

            if (exception is TwitchAccessTokenUnavailableException)
                status.SetAuthorized(false);

            var report = new TwitchRuntimeSessionHealthReport.Unhealthy
            {
                Runtime = runtime,
                Classification = classification,
                Attempt = established.Attempt,
                Exception = exception,
            };
            health.Report(report);
            return new TwitchRuntimeListenOutcome.Unhealthy { Report = report };
        }
    }

    private static ValueTask DisposeHandoffAsync(TwitchRuntimeSessionHandoff handoff) =>
        handoff switch
        {
            TwitchRuntimeSessionHandoff.None => ValueTask.CompletedTask,
            TwitchRuntimeSessionHandoff.Pending pending => pending.Session.DisposeAsync(),
            _ => throw new UnreachableException("Unknown runtime session handoff."),
        };

    private static async ValueTask<bool> WaitForChannelsAsync(
        ITwitchRuntimeIdleWait idleWait,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await idleWait.WaitAsync(stoppingToken);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static TwitchRuntimeSessionHealthReport.Unhealthy CreateUnhealthyReport(
        TwitchBotRuntime runtime,
        Func<
            Exception,
            CancellationToken,
            TwitchRuntimeSessionFailureClassification
        > classify,
        int attempt,
        Exception exception,
        CancellationToken cancellationToken
    ) =>
        new()
        {
            Runtime = runtime,
            Classification = classify(exception, cancellationToken),
            Attempt = attempt,
            Exception = exception,
        };
}

internal abstract record TwitchRuntimeConnectionTarget
{
    private protected TwitchRuntimeConnectionTarget() { }

    private protected abstract void Seal();

    internal sealed record Initial : TwitchRuntimeConnectionTarget
    {
        private protected override void Seal() { }
    }

    internal sealed record EventSubReconnect : TwitchRuntimeConnectionTarget
    {
        internal required Uri Uri { get; init; }

        private protected override void Seal() { }
    }
}

internal abstract record TwitchRuntimeSessionEstablishment
{
    private protected TwitchRuntimeSessionEstablishment() { }

    private protected abstract void Seal();

    internal sealed record Idle : TwitchRuntimeSessionEstablishment
    {
        private protected override void Seal() { }
    }

    internal sealed record Established : TwitchRuntimeSessionEstablishment
    {
        internal required ITwitchRuntimeEstablishedSession Session { get; init; }

        private protected override void Seal() { }
    }
}

internal interface ITwitchRuntimeEstablishedSession : IAsyncDisposable
{
    Task<TwitchRuntimeReconnectRequest> ListenAsync(CancellationToken cancellationToken);
}

internal sealed record TwitchRuntimeReconnectRequest
{
    internal required TwitchRuntimeConnectionTarget Target { get; init; }
}

internal abstract record TwitchRuntimeSessionOutcome
{
    private protected TwitchRuntimeSessionOutcome() { }

    private protected abstract void Seal();

    internal sealed record Idle : TwitchRuntimeSessionOutcome
    {
        private protected override void Seal() { }
    }

    internal sealed record Established : TwitchRuntimeSessionOutcome
    {
        internal required ITwitchRuntimeEstablishedSession Session { get; init; }

        internal required int Attempt { get; init; }

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

internal abstract record TwitchRuntimeListenOutcome
{
    private protected TwitchRuntimeListenOutcome() { }

    private protected abstract void Seal();

    internal sealed record Reconnect : TwitchRuntimeListenOutcome
    {
        internal required TwitchRuntimeConnectionTarget Target { get; init; }

        private protected override void Seal() { }
    }

    internal sealed record ProtocolHandoff : TwitchRuntimeListenOutcome
    {
        internal required TwitchRuntimeConnectionTarget Target { get; init; }

        internal required ITwitchRuntimeEstablishedSession PreviousSession { get; init; }

        private protected override void Seal() { }
    }

    internal sealed record Canceled : TwitchRuntimeListenOutcome
    {
        private protected override void Seal() { }
    }

    internal sealed record Unhealthy : TwitchRuntimeListenOutcome
    {
        internal required TwitchRuntimeSessionHealthReport.Unhealthy Report { get; init; }

        private protected override void Seal() { }
    }
}

internal abstract record TwitchRuntimeSessionHandoff
{
    private protected TwitchRuntimeSessionHandoff() { }

    private protected abstract void Seal();

    internal sealed record None : TwitchRuntimeSessionHandoff
    {
        private protected override void Seal() { }
    }

    internal sealed record Pending : TwitchRuntimeSessionHandoff
    {
        internal required ITwitchRuntimeEstablishedSession Session { get; init; }

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
