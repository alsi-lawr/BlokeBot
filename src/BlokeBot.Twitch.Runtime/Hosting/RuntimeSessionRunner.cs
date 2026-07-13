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
                    var shouldContinue = await outcome.Match(
                        HandleIdleAsync,
                        HandleEstablishedAsync,
                        static _ => ValueTask.FromResult(false),
                        static _ => ValueTask.FromResult(false)
                    );
                    if (!shouldContinue)
                    {
                        return;
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

        async ValueTask<bool> HandleIdleAsync(RuntimeSessionOutcome.Idle _)
        {
            var idleHandoff = handoff;
            handoff = new RuntimeSessionHandoff.None();
            await DisposeHandoffAsync(idleHandoff);
            target = initialTarget;
            return await WaitForChannelsAsync(idleWait, stoppingToken);
        }

        async ValueTask<bool> HandleEstablishedAsync(RuntimeSessionOutcome.Established established)
        {
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
            return nextTarget.Match(
                reconnect =>
                {
                    target = reconnect.Target;
                    return true;
                },
                protocol =>
                {
                    target = protocol.Target;
                    handoff = new RuntimeSessionHandoff.Pending
                    {
                        Session = protocol.PreviousSession,
                        Attempt = protocol.Attempt,
                    };
                    return true;
                },
                static _ => false,
                static _ => false
            );
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
                await establishment.Match(
                    static _ => ValueTask.CompletedTask,
                    connected => connected.Session.DisposeAsync()
                );

                return new RuntimeSessionOutcome.Canceled();
            }

            return establishment.Match<RuntimeSessionOutcome>(
                static _ => new RuntimeSessionOutcome.Idle(),
                established => new RuntimeSessionOutcome.Established
                {
                    Session = established.Session,
                    Attempt = attempt,
                }
            );
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

    private static ValueTask DisposeHandoffAsync(RuntimeSessionHandoff handoff)
    {
        return handoff.Match(static _ => ValueTask.CompletedTask, DisposePendingAsync);

        static async ValueTask DisposePendingAsync(RuntimeSessionHandoff.Pending pending)
        {
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

    internal abstract TResult Match<TResult>(
        Func<Initial, TResult> initial,
        Func<EventSubReconnect, TResult> eventSubReconnect
    );

    internal sealed record Initial : RuntimeConnectionTarget
    {
        internal override TResult Match<TResult>(
            Func<Initial, TResult> initial,
            Func<EventSubReconnect, TResult> eventSubReconnect
        )
        {
            return initial(this);
        }
    }

    internal sealed record EventSubReconnect : RuntimeConnectionTarget
    {
        internal required Uri Uri { get; init; }

        internal override TResult Match<TResult>(
            Func<Initial, TResult> initial,
            Func<EventSubReconnect, TResult> eventSubReconnect
        )
        {
            return eventSubReconnect(this);
        }
    }
}

internal abstract record RuntimeSessionEstablishment
{
    private RuntimeSessionEstablishment() { }

    internal abstract TResult Match<TResult>(
        Func<Idle, TResult> idle,
        Func<Established, TResult> established
    );

    internal sealed record Idle : RuntimeSessionEstablishment
    {
        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established
        )
        {
            return idle(this);
        }
    }

    internal sealed record Established : RuntimeSessionEstablishment
    {
        internal required IRuntimeEstablishedSession Session { get; init; }

        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established
        )
        {
            return established(this);
        }
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

    internal abstract TResult Match<TResult>(
        Func<Idle, TResult> idle,
        Func<Established, TResult> established,
        Func<Canceled, TResult> canceled,
        Func<Unhealthy, TResult> unhealthy
    );

    internal sealed record Idle : RuntimeSessionOutcome
    {
        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        )
        {
            return idle(this);
        }
    }

    internal sealed record Established : RuntimeSessionOutcome
    {
        internal required IRuntimeEstablishedSession Session { get; init; }

        internal required int Attempt { get; init; }

        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        )
        {
            return established(this);
        }
    }

    internal sealed record Canceled : RuntimeSessionOutcome
    {
        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        )
        {
            return canceled(this);
        }
    }

    internal sealed record Unhealthy : RuntimeSessionOutcome
    {
        internal required RuntimeSessionHealthReport.Unhealthy Report { get; init; }

        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        )
        {
            return unhealthy(this);
        }
    }
}

internal abstract record RuntimeListenOutcome
{
    private RuntimeListenOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Reconnect, TResult> reconnect,
        Func<ProtocolHandoff, TResult> protocolHandoff,
        Func<Canceled, TResult> canceled,
        Func<Unhealthy, TResult> unhealthy
    );

    internal sealed record Reconnect : RuntimeListenOutcome
    {
        internal required RuntimeConnectionTarget Target { get; init; }

        internal override TResult Match<TResult>(
            Func<Reconnect, TResult> reconnect,
            Func<ProtocolHandoff, TResult> protocolHandoff,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        )
        {
            return reconnect(this);
        }
    }

    internal sealed record ProtocolHandoff : RuntimeListenOutcome
    {
        internal required RuntimeConnectionTarget Target { get; init; }

        internal required IRuntimeEstablishedSession PreviousSession { get; init; }

        internal required int Attempt { get; init; }

        internal override TResult Match<TResult>(
            Func<Reconnect, TResult> reconnect,
            Func<ProtocolHandoff, TResult> protocolHandoff,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        )
        {
            return protocolHandoff(this);
        }
    }

    internal sealed record Canceled : RuntimeListenOutcome
    {
        internal override TResult Match<TResult>(
            Func<Reconnect, TResult> reconnect,
            Func<ProtocolHandoff, TResult> protocolHandoff,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        )
        {
            return canceled(this);
        }
    }

    internal sealed record Unhealthy : RuntimeListenOutcome
    {
        internal required RuntimeSessionHealthReport.Unhealthy Report { get; init; }

        internal override TResult Match<TResult>(
            Func<Reconnect, TResult> reconnect,
            Func<ProtocolHandoff, TResult> protocolHandoff,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        )
        {
            return unhealthy(this);
        }
    }
}

internal abstract record RuntimeSessionHandoff
{
    private RuntimeSessionHandoff() { }

    internal abstract TResult Match<TResult>(
        Func<None, TResult> none,
        Func<Pending, TResult> pending
    );

    internal sealed record None : RuntimeSessionHandoff
    {
        internal override TResult Match<TResult>(
            Func<None, TResult> none,
            Func<Pending, TResult> pending
        )
        {
            return none(this);
        }
    }

    internal sealed record Pending : RuntimeSessionHandoff
    {
        internal required IRuntimeEstablishedSession Session { get; init; }

        internal required int Attempt { get; init; }

        internal override TResult Match<TResult>(
            Func<None, TResult> none,
            Func<Pending, TResult> pending
        )
        {
            return pending(this);
        }
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
