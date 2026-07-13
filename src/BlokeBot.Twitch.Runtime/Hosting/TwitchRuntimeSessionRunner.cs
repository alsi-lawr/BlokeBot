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
        Func<Exception, CancellationToken, TwitchRuntimeSessionFailureClassification> classify,
        ITwitchRuntimeSessionHealthReporter health,
        TwitchBotRuntimeStatusStore status,
        ITwitchRuntimeIdleWait idleWait,
        CancellationToken stoppingToken
    )
    {
        var target = initialTarget;
        var currentAttempt = 1;
        TwitchRuntimeSessionHandoff handoff = new TwitchRuntimeSessionHandoff.None();
        try
        {
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
                            target = initialTarget;
                            if (!await WaitForChannelsAsync(idleWait, stoppingToken))
                            {
                                return;
                            }

                            break;
                        case TwitchRuntimeSessionOutcome.Established established:
                            var completedHandoff = handoff;
                            handoff = new TwitchRuntimeSessionHandoff.None();
                            currentAttempt = established.Attempt;
                            await CompleteHandoffAsync(completedHandoff, established);
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
                                        Attempt = protocol.Attempt,
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            status.SetConnected(false, []);
            var attempt = exception is TwitchRuntimeSessionCleanupException cleanup
                ? cleanup.Attempt
                : currentAttempt;
            var report = CreateUnhealthyReport(
                runtime,
                classify,
                attempt,
                exception,
                CancellationToken.None
            );
            health.Report(report);
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
        Func<Exception, CancellationToken, TwitchRuntimeSessionFailureClassification> classify,
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
                {
                    await connected.Session.DisposeAsync();
                }

                return new TwitchRuntimeSessionOutcome.Canceled();
            }

            return establishment switch
            {
                TwitchRuntimeSessionEstablishment.Idle => new TwitchRuntimeSessionOutcome.Idle(),
                TwitchRuntimeSessionEstablishment.Established established =>
                    new TwitchRuntimeSessionOutcome.Established
                    {
                        Session = established.Session,
                        Attempt = attempt,
                    },
                _ => throw new UnreachableException("Unknown runtime session establishment."),
            };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return new TwitchRuntimeSessionOutcome.Canceled();
        }
        catch (Exception exception)
        {
            if (exception is AccessTokenUnavailableException)
            {
                status.SetAuthorized(false);
            }

            var report = CreateUnhealthyReport(
                runtime,
                classify,
                attempt,
                exception,
                stoppingToken
            );
            health.Report(report);
            return new TwitchRuntimeSessionOutcome.Unhealthy { Report = report };
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
        Func<Exception, CancellationToken, TwitchRuntimeSessionFailureClassification> classify,
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
                Attempt = established.Attempt,
            };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            try
            {
                await session.DisposeAsync();
                return new TwitchRuntimeListenOutcome.Canceled();
            }
            catch (Exception cleanupException)
            {
                status.SetConnected(false, []);
                var report = new TwitchRuntimeSessionHealthReport.Unhealthy
                {
                    Runtime = runtime,
                    Classification = TwitchRuntimeSessionFailureClassification.Unexpected,
                    Attempt = established.Attempt,
                    Exception = new TwitchRuntimeSessionCleanupException(
                        established.Attempt,
                        "Runtime session cleanup failed during cancellation.",
                        cleanupException
                    ),
                };
                health.Report(report);
                return new TwitchRuntimeListenOutcome.Unhealthy { Report = report };
            }
        }
        catch (Exception exception)
        {
            if (exception is AccessTokenUnavailableException)
            {
                status.SetAuthorized(false);
            }

            var failure = await IncludeCleanupFailureAsync(session, established.Attempt, exception);
            status.SetConnected(false, []);
            var classification = classify(failure, stoppingToken);
            if (TwitchRuntimeSessionFailureClassifier.IsRetryable(classification))
            {
                health.Report(
                    new TwitchRuntimeSessionHealthReport.ReconnectScheduled
                    {
                        Runtime = runtime,
                        Classification = classification,
                        Attempt = established.Attempt,
                        Exception = failure,
                    }
                );
                return new TwitchRuntimeListenOutcome.Reconnect
                {
                    Target = new TwitchRuntimeConnectionTarget.Initial(),
                };
            }

            var report = new TwitchRuntimeSessionHealthReport.Unhealthy
            {
                Runtime = runtime,
                Classification = classification,
                Attempt = established.Attempt,
                Exception = failure,
            };
            health.Report(report);
            return new TwitchRuntimeListenOutcome.Unhealthy { Report = report };
        }
    }

    private static async ValueTask<Exception> IncludeCleanupFailureAsync(
        ITwitchRuntimeEstablishedSession session,
        int attempt,
        Exception listeningException
    )
    {
        try
        {
            await session.DisposeAsync();
            return listeningException;
        }
        catch (Exception cleanupException)
        {
            return new AggregateException(
                "Runtime session listening and cleanup both failed.",
                listeningException,
                new TwitchRuntimeSessionCleanupException(
                    attempt,
                    "Runtime session cleanup failed after a listening failure.",
                    cleanupException
                )
            );
        }
    }

    private static async ValueTask DisposeHandoffAsync(TwitchRuntimeSessionHandoff handoff)
    {
        switch (handoff)
        {
            case TwitchRuntimeSessionHandoff.None:
                return;
            case TwitchRuntimeSessionHandoff.Pending pending:
                try
                {
                    await pending.Session.DisposeAsync();
                    return;
                }
                catch (Exception exception)
                {
                    throw new TwitchRuntimeSessionCleanupException(
                        pending.Attempt,
                        "EventSub protocol handoff cleanup failed.",
                        exception
                    );
                }
            default:
                throw new UnreachableException("Unknown runtime session handoff.");
        }
    }

    private static async ValueTask CompleteHandoffAsync(
        TwitchRuntimeSessionHandoff handoff,
        TwitchRuntimeSessionOutcome.Established replacement
    )
    {
        try
        {
            await DisposeHandoffAsync(handoff);
        }
        catch (Exception handoffException)
        {
            try
            {
                await replacement.Session.DisposeAsync();
            }
            catch (Exception replacementException)
            {
                var attempt = handoffException is TwitchRuntimeSessionCleanupException cleanup
                    ? cleanup.Attempt
                    : replacement.Attempt;
                throw new TwitchRuntimeSessionCleanupException(
                    attempt,
                    "EventSub protocol handoff and replacement session cleanup both failed.",
                    new AggregateException(
                        handoffException,
                        new TwitchRuntimeSessionCleanupException(
                            replacement.Attempt,
                            "Replacement runtime session cleanup failed after EventSub protocol handoff cleanup.",
                            replacementException
                        )
                    )
                );
            }

            throw;
        }
    }

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
        Func<Exception, CancellationToken, TwitchRuntimeSessionFailureClassification> classify,
        int attempt,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        return new()
        {
            Runtime = runtime,
            Classification = classify(exception, cancellationToken),
            Attempt = attempt,
            Exception = exception,
        };
    }
}

internal abstract record TwitchRuntimeConnectionTarget
{
    private TwitchRuntimeConnectionTarget() { }

    internal sealed record Initial : TwitchRuntimeConnectionTarget;

    internal sealed record EventSubReconnect : TwitchRuntimeConnectionTarget
    {
        internal required Uri Uri { get; init; }
    }
}

internal abstract record TwitchRuntimeSessionEstablishment
{
    private TwitchRuntimeSessionEstablishment() { }

    internal sealed record Idle : TwitchRuntimeSessionEstablishment;

    internal sealed record Established : TwitchRuntimeSessionEstablishment
    {
        internal required ITwitchRuntimeEstablishedSession Session { get; init; }
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
    private TwitchRuntimeSessionOutcome() { }

    internal sealed record Idle : TwitchRuntimeSessionOutcome;

    internal sealed record Established : TwitchRuntimeSessionOutcome
    {
        internal required ITwitchRuntimeEstablishedSession Session { get; init; }

        internal required int Attempt { get; init; }
    }

    internal sealed record Canceled : TwitchRuntimeSessionOutcome;

    internal sealed record Unhealthy : TwitchRuntimeSessionOutcome
    {
        internal required TwitchRuntimeSessionHealthReport.Unhealthy Report { get; init; }
    }
}

internal abstract record TwitchRuntimeListenOutcome
{
    private TwitchRuntimeListenOutcome() { }

    internal sealed record Reconnect : TwitchRuntimeListenOutcome
    {
        internal required TwitchRuntimeConnectionTarget Target { get; init; }
    }

    internal sealed record ProtocolHandoff : TwitchRuntimeListenOutcome
    {
        internal required TwitchRuntimeConnectionTarget Target { get; init; }

        internal required ITwitchRuntimeEstablishedSession PreviousSession { get; init; }

        internal required int Attempt { get; init; }
    }

    internal sealed record Canceled : TwitchRuntimeListenOutcome;

    internal sealed record Unhealthy : TwitchRuntimeListenOutcome
    {
        internal required TwitchRuntimeSessionHealthReport.Unhealthy Report { get; init; }
    }
}

internal abstract record TwitchRuntimeSessionHandoff
{
    private TwitchRuntimeSessionHandoff() { }

    internal sealed record None : TwitchRuntimeSessionHandoff;

    internal sealed record Pending : TwitchRuntimeSessionHandoff
    {
        internal required ITwitchRuntimeEstablishedSession Session { get; init; }

        internal required int Attempt { get; init; }
    }
}

internal sealed class TwitchRuntimeSessionCleanupException(
    int attempt,
    string message,
    Exception innerException
) : Exception(message, innerException)
{
    internal int Attempt { get; } = attempt;
}

internal interface ITwitchRuntimeIdleWait
{
    ValueTask WaitAsync(CancellationToken cancellationToken);
}

internal sealed class TwitchRuntimeIdleWait(TimeProvider timeProvider) : ITwitchRuntimeIdleWait
{
    private static readonly TimeSpan _idleInterval = TimeSpan.FromSeconds(30);

    public ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        return new(Task.Delay(_idleInterval, timeProvider, cancellationToken));
    }
}
