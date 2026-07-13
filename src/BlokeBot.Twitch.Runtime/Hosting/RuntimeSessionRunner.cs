using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal static class RuntimeSessionRunner
{
    internal static async Task RunUntilStoppedAsync(
        ChatRuntime runtime,
        RuntimeConnectionTarget initialTarget,
        Func<
            RuntimeConnectionTarget,
            CancellationToken,
            Task<RuntimeSessionOutcome>
        > establishSession,
        Func<Exception, CancellationToken, RuntimeSessionFailureClassification> classify,
        IRuntimeSessionHealthReporter health,
        BotRuntimeStatusStore status,
        IRuntimeIdleWait idleWait,
        CancellationToken stoppingToken
    )
    {
        var target = initialTarget;
        var currentAttempt = 1;
        RuntimeSessionHandoff handoff = new RuntimeSessionHandoff.None();
        try
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var outcome = await establishSession(target, stoppingToken);
                    switch (outcome)
                    {
                        case RuntimeSessionOutcome.Idle:
                            var idleHandoff = handoff;
                            handoff = new RuntimeSessionHandoff.None();
                            await DisposeHandoffAsync(idleHandoff);
                            target = initialTarget;
                            if (!await WaitForChannelsAsync(idleWait, stoppingToken))
                            {
                                return;
                            }

                            break;
                        case RuntimeSessionOutcome.Established established:
                            var completedHandoff = handoff;
                            handoff = new RuntimeSessionHandoff.None();
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
                                case RuntimeListenOutcome.Reconnect reconnect:
                                    target = reconnect.Target;
                                    break;
                                case RuntimeListenOutcome.ProtocolHandoff protocol:
                                    target = protocol.Target;
                                    handoff = new RuntimeSessionHandoff.Pending
                                    {
                                        Session = protocol.PreviousSession,
                                        Attempt = protocol.Attempt,
                                    };
                                    break;
                                case RuntimeListenOutcome.Canceled:
                                case RuntimeListenOutcome.Unhealthy:
                                    return;
                                default:
                                    throw new UnreachableException(
                                        "Unknown runtime listening outcome."
                                    );
                            }
                            break;
                        case RuntimeSessionOutcome.Canceled:
                        case RuntimeSessionOutcome.Unhealthy:
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
            var attempt = exception is RuntimeSessionCleanupException cleanup
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

    internal static async Task<RuntimeSessionOutcome> EstablishOnceAsync(
        ChatRuntime runtime,
        Func<CancellationToken, Task<RuntimeSessionEstablishment>> establishSession,
        Func<
            Func<CancellationToken, Task<RuntimeSessionEstablishment>>,
            CancellationToken,
            ValueTask<RuntimeSessionEstablishment>
        > execute,
        Func<Exception, CancellationToken, RuntimeSessionFailureClassification> classify,
        IRuntimeSessionHealthReporter health,
        BotRuntimeStatusStore status,
        CancellationToken stoppingToken
    )
    {
        var attempt = 0;
        try
        {
            var establishment = await execute(RunAttemptAsync, stoppingToken);
            if (stoppingToken.IsCancellationRequested)
            {
                if (establishment is RuntimeSessionEstablishment.Established connected)
                {
                    await connected.Session.DisposeAsync();
                }

                return new RuntimeSessionOutcome.Canceled();
            }

            return establishment switch
            {
                RuntimeSessionEstablishment.Idle => new RuntimeSessionOutcome.Idle(),
                RuntimeSessionEstablishment.Established established =>
                    new RuntimeSessionOutcome.Established
                    {
                        Session = established.Session,
                        Attempt = attempt,
                    },
                _ => throw new UnreachableException("Unknown runtime session establishment."),
            };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return new RuntimeSessionOutcome.Canceled();
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
            return new RuntimeSessionOutcome.Unhealthy { Report = report };
        }

        async Task<RuntimeSessionEstablishment> RunAttemptAsync(CancellationToken attemptToken)
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

    private static async Task<RuntimeListenOutcome> ListenAsync(
        ChatRuntime runtime,
        RuntimeSessionOutcome.Established established,
        Func<Exception, CancellationToken, RuntimeSessionFailureClassification> classify,
        IRuntimeSessionHealthReporter health,
        BotRuntimeStatusStore status,
        CancellationToken stoppingToken
    )
    {
        var session = established.Session;
        try
        {
            var reconnect = await session.ListenAsync(stoppingToken);
            return new RuntimeListenOutcome.ProtocolHandoff
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
                return new RuntimeListenOutcome.Canceled();
            }
            catch (Exception cleanupException)
            {
                status.SetConnected(false, []);
                var report = new RuntimeSessionHealthReport.Unhealthy
                {
                    Runtime = runtime,
                    Classification = RuntimeSessionFailureClassification.Unexpected,
                    Attempt = established.Attempt,
                    Exception = new RuntimeSessionCleanupException(
                        established.Attempt,
                        "Runtime session cleanup failed during cancellation.",
                        cleanupException
                    ),
                };
                health.Report(report);
                return new RuntimeListenOutcome.Unhealthy { Report = report };
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
            if (RuntimeSessionFailureClassifier.IsRetryable(classification))
            {
                health.Report(
                    new RuntimeSessionHealthReport.ReconnectScheduled
                    {
                        Runtime = runtime,
                        Classification = classification,
                        Attempt = established.Attempt,
                        Exception = failure,
                    }
                );
                return new RuntimeListenOutcome.Reconnect
                {
                    Target = new RuntimeConnectionTarget.Initial(),
                };
            }

            var report = new RuntimeSessionHealthReport.Unhealthy
            {
                Runtime = runtime,
                Classification = classification,
                Attempt = established.Attempt,
                Exception = failure,
            };
            health.Report(report);
            return new RuntimeListenOutcome.Unhealthy { Report = report };
        }
    }

    private static async ValueTask<Exception> IncludeCleanupFailureAsync(
        IRuntimeEstablishedSession session,
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
                new RuntimeSessionCleanupException(
                    attempt,
                    "Runtime session cleanup failed after a listening failure.",
                    cleanupException
                )
            );
        }
    }

    private static async ValueTask DisposeHandoffAsync(RuntimeSessionHandoff handoff)
    {
        switch (handoff)
        {
            case RuntimeSessionHandoff.None:
                return;
            case RuntimeSessionHandoff.Pending pending:
                try
                {
                    await pending.Session.DisposeAsync();
                    return;
                }
                catch (Exception exception)
                {
                    throw new RuntimeSessionCleanupException(
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
        RuntimeSessionHandoff handoff,
        RuntimeSessionOutcome.Established replacement
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
                var attempt = handoffException is RuntimeSessionCleanupException cleanup
                    ? cleanup.Attempt
                    : replacement.Attempt;
                throw new RuntimeSessionCleanupException(
                    attempt,
                    "EventSub protocol handoff and replacement session cleanup both failed.",
                    new AggregateException(
                        handoffException,
                        new RuntimeSessionCleanupException(
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
        IRuntimeIdleWait idleWait,
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

    private static RuntimeSessionHealthReport.Unhealthy CreateUnhealthyReport(
        ChatRuntime runtime,
        Func<Exception, CancellationToken, RuntimeSessionFailureClassification> classify,
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

internal abstract record RuntimeConnectionTarget
{
    private RuntimeConnectionTarget() { }

    internal sealed record Initial : RuntimeConnectionTarget;

    internal sealed record EventSubReconnect : RuntimeConnectionTarget
    {
        internal required Uri Uri { get; init; }
    }
}

internal abstract record RuntimeSessionEstablishment
{
    private RuntimeSessionEstablishment() { }

    internal sealed record Idle : RuntimeSessionEstablishment;

    internal sealed record Established : RuntimeSessionEstablishment
    {
        internal required IRuntimeEstablishedSession Session { get; init; }
    }
}

internal interface IRuntimeEstablishedSession : IAsyncDisposable
{
    Task<RuntimeReconnectRequest> ListenAsync(CancellationToken cancellationToken);
}

internal sealed record RuntimeReconnectRequest
{
    internal required RuntimeConnectionTarget Target { get; init; }
}

internal abstract record RuntimeSessionOutcome
{
    private RuntimeSessionOutcome() { }

    internal sealed record Idle : RuntimeSessionOutcome;

    internal sealed record Established : RuntimeSessionOutcome
    {
        internal required IRuntimeEstablishedSession Session { get; init; }

        internal required int Attempt { get; init; }
    }

    internal sealed record Canceled : RuntimeSessionOutcome;

    internal sealed record Unhealthy : RuntimeSessionOutcome
    {
        internal required RuntimeSessionHealthReport.Unhealthy Report { get; init; }
    }
}

internal abstract record RuntimeListenOutcome
{
    private RuntimeListenOutcome() { }

    internal sealed record Reconnect : RuntimeListenOutcome
    {
        internal required RuntimeConnectionTarget Target { get; init; }
    }

    internal sealed record ProtocolHandoff : RuntimeListenOutcome
    {
        internal required RuntimeConnectionTarget Target { get; init; }

        internal required IRuntimeEstablishedSession PreviousSession { get; init; }

        internal required int Attempt { get; init; }
    }

    internal sealed record Canceled : RuntimeListenOutcome;

    internal sealed record Unhealthy : RuntimeListenOutcome
    {
        internal required RuntimeSessionHealthReport.Unhealthy Report { get; init; }
    }
}

internal abstract record RuntimeSessionHandoff
{
    private RuntimeSessionHandoff() { }

    internal sealed record None : RuntimeSessionHandoff;

    internal sealed record Pending : RuntimeSessionHandoff
    {
        internal required IRuntimeEstablishedSession Session { get; init; }

        internal required int Attempt { get; init; }
    }
}

internal sealed class RuntimeSessionCleanupException(
    int attempt,
    string message,
    Exception innerException
) : Exception(message, innerException)
{
    internal int Attempt { get; } = attempt;
}

internal interface IRuntimeIdleWait
{
    ValueTask WaitAsync(CancellationToken cancellationToken);
}

internal sealed class RuntimeIdleWait(TimeProvider timeProvider) : IRuntimeIdleWait
{
    private static readonly TimeSpan _idleInterval = TimeSpan.FromSeconds(30);

    public ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        return new(Task.Delay(_idleInterval, timeProvider, cancellationToken));
    }
}
