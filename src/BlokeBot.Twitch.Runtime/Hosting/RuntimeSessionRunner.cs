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
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var outcome = await establishSession(target, stoppingToken);
                var shouldContinue = await outcome.Match(
                    HandleIdleAsync,
                    HandleEstablishedAsync,
                    static _ => ValueTask.FromResult(false),
                    static _ => ValueTask.FromResult(false),
                    static _ => ValueTask.FromResult(false)
                );
                if (!shouldContinue)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            status.MarkDisconnected();
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
            target = initialTarget;
            return await WaitForChannelsAsync(idleWait, stoppingToken);
        }

        async ValueTask<bool> HandleEstablishedAsync(RuntimeSessionOutcome.Established established)
        {
            currentAttempt = established.Attempt;
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
                    connected => connected.Session.DisposeAsync(),
                    static _ => ValueTask.CompletedTask
                );

                return new RuntimeSessionOutcome.Canceled();
            }

            return establishment.Match<RuntimeSessionOutcome>(
                static _ => new RuntimeSessionOutcome.Idle(),
                established => new RuntimeSessionOutcome.Established
                {
                    Session = established.Session,
                    Attempt = attempt,
                },
                unavailable => TokenUnavailable(unavailable.Reason)
            );

            RuntimeSessionOutcome TokenUnavailable(AccessTokenUnavailableReason reason)
            {
                status.MarkUnauthorized();
                return new RuntimeSessionOutcome.TokenUnavailable(reason);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return new RuntimeSessionOutcome.Canceled();
        }
        catch (Exception exception)
        {
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
                status.MarkDisconnected();
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
            await session.DisposeAsync();
            return new RuntimeListenOutcome.Reconnect { Target = reconnect.Target };
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
                status.MarkDisconnected();
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
            var failure = await IncludeCleanupFailureAsync(session, established.Attempt, exception);
            status.MarkDisconnected();
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
                    Target = new RuntimeConnectionTarget(),
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
    ) =>
        new()
        {
            Runtime = runtime,
            Classification = classify(exception, cancellationToken),
            Attempt = attempt,
            Exception = exception,
        };
}

internal sealed record RuntimeConnectionTarget;

internal abstract record RuntimeSessionEstablishment
{
    private RuntimeSessionEstablishment() { }

    internal abstract TResult Match<TResult>(
        Func<Idle, TResult> idle,
        Func<Established, TResult> established,
        Func<TokenUnavailable, TResult> tokenUnavailable
    );

    internal sealed record Idle : RuntimeSessionEstablishment
    {
        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<TokenUnavailable, TResult> tokenUnavailable
        ) => idle(this);
    }

    internal sealed record Established : RuntimeSessionEstablishment
    {
        internal required IRuntimeEstablishedSession Session { get; init; }

        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<TokenUnavailable, TResult> tokenUnavailable
        ) => established(this);
    }

    internal sealed record TokenUnavailable(AccessTokenUnavailableReason Reason)
        : RuntimeSessionEstablishment
    {
        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<TokenUnavailable, TResult> tokenUnavailable
        ) => tokenUnavailable(this);
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
        Func<TokenUnavailable, TResult> tokenUnavailable,
        Func<Unhealthy, TResult> unhealthy
    );

    internal sealed record Idle : RuntimeSessionOutcome
    {
        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<Canceled, TResult> canceled,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<Unhealthy, TResult> unhealthy
        ) => idle(this);
    }

    internal sealed record Established : RuntimeSessionOutcome
    {
        internal required IRuntimeEstablishedSession Session { get; init; }

        internal required int Attempt { get; init; }

        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<Canceled, TResult> canceled,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<Unhealthy, TResult> unhealthy
        ) => established(this);
    }

    internal sealed record Canceled : RuntimeSessionOutcome
    {
        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<Canceled, TResult> canceled,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<Unhealthy, TResult> unhealthy
        ) => canceled(this);
    }

    internal sealed record TokenUnavailable(AccessTokenUnavailableReason Reason)
        : RuntimeSessionOutcome
    {
        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<Canceled, TResult> canceled,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<Unhealthy, TResult> unhealthy
        ) => tokenUnavailable(this);
    }

    internal sealed record Unhealthy : RuntimeSessionOutcome
    {
        internal required RuntimeSessionHealthReport.Unhealthy Report { get; init; }

        internal override TResult Match<TResult>(
            Func<Idle, TResult> idle,
            Func<Established, TResult> established,
            Func<Canceled, TResult> canceled,
            Func<TokenUnavailable, TResult> tokenUnavailable,
            Func<Unhealthy, TResult> unhealthy
        ) => unhealthy(this);
    }
}

internal abstract record RuntimeListenOutcome
{
    private RuntimeListenOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Reconnect, TResult> reconnect,
        Func<Canceled, TResult> canceled,
        Func<Unhealthy, TResult> unhealthy
    );

    internal sealed record Reconnect : RuntimeListenOutcome
    {
        internal required RuntimeConnectionTarget Target { get; init; }

        internal override TResult Match<TResult>(
            Func<Reconnect, TResult> reconnect,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        ) => reconnect(this);
    }

    internal sealed record Canceled : RuntimeListenOutcome
    {
        internal override TResult Match<TResult>(
            Func<Reconnect, TResult> reconnect,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        ) => canceled(this);
    }

    internal sealed record Unhealthy : RuntimeListenOutcome
    {
        internal required RuntimeSessionHealthReport.Unhealthy Report { get; init; }

        internal override TResult Match<TResult>(
            Func<Reconnect, TResult> reconnect,
            Func<Canceled, TResult> canceled,
            Func<Unhealthy, TResult> unhealthy
        ) => unhealthy(this);
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

    public ValueTask WaitAsync(CancellationToken cancellationToken) =>
        new(Task.Delay(_idleInterval, timeProvider, cancellationToken));
}
