using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace BlokeBot.Twitch.Runtime;

internal interface ITwitchEventSubChannelOperations
{
    ValueTask<TwitchBotAccount> ResolveAccountAsync(
        string channel,
        CancellationToken cancellationToken
    );

    ValueTask<ActiveEventSubSubscription> CreateSubscriptionAsync(
        string channel,
        TwitchBotAccount account,
        string sessionId,
        CancellationToken cancellationToken
    );

    ValueTask CompleteSetupAsync(string channel, CancellationToken cancellationToken);

    ValueTask DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken cancellationToken
    );

    ValueTask CompleteStopAsync(string channel, CancellationToken cancellationToken);
}

internal sealed class TwitchEventSubChannelOperations(
    TwitchBotSettings settings,
    ITwitchBotAccountProvider accounts,
    TwitchHelixChatClient helix,
    ITwitchChatMessageSender sender,
    ITwitchBotChannelLifecycleNotifier lifecycle
) : ITwitchEventSubChannelOperations
{
    public ValueTask<TwitchBotAccount> ResolveAccountAsync(
        string channel,
        CancellationToken cancellationToken
    ) => accounts.GetBotAccountAsync(channel, cancellationToken);

    public async ValueTask<ActiveEventSubSubscription> CreateSubscriptionAsync(
        string channel,
        TwitchBotAccount account,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        var identities = await helix.ResolveChatIdentitiesAsync(
            channel,
            account.Login,
            account.AccessToken,
            cancellationToken
        );
        return new ActiveEventSubSubscription
        {
            Channel = channel,
            SubscriptionId = await helix.CreateChatMessageSubscriptionAsync(
                account.AccessToken,
                identities.BroadcasterId,
                identities.BotUserId,
                sessionId,
                cancellationToken
            ),
            BotLogin = account.Login,
            AccessToken = account.AccessToken,
            Readiness = TwitchEventSubSubscriptionReadiness.PendingSetup,
        };
    }

    public async ValueTask CompleteSetupAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(settings.StartupMessage))
            await sender.SendAsync(channel, settings.StartupMessage, cancellationToken);

        await lifecycle.ChannelStartedAsync(channel, cancellationToken);
    }

    public ValueTask DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken cancellationToken
    ) =>
        new(
            helix.DeleteEventSubSubscriptionAsync(
                subscription.AccessToken,
                subscription.SubscriptionId,
                cancellationToken
            )
        );

    public ValueTask CompleteStopAsync(
        string channel,
        CancellationToken cancellationToken
    ) => new(lifecycle.ChannelStoppedAsync(channel, cancellationToken));
}

internal sealed class TwitchEventSubChannelSessionFactory(
    ITwitchEventSubChannelOperations operations,
    TwitchEventSubChannelRecoveryPipeline recovery,
    TwitchEventSubChannelStatusStore channelStatus,
    TwitchBotRuntimeStatusStore runtimeStatus,
    ITwitchEventSubChannelDiagnosticReporter diagnostics,
    TimeProvider timeProvider
)
{
    internal TwitchEventSubChannelSession Create(string sessionId) =>
        new(
            sessionId,
            operations,
            recovery,
            channelStatus.CreateScope(),
            runtimeStatus,
            diagnostics,
            timeProvider
        );
}

internal sealed class TwitchEventSubChannelSession(
    string sessionId,
    ITwitchEventSubChannelOperations operations,
    TwitchEventSubChannelRecoveryPipeline recovery,
    TwitchEventSubChannelStatusStore.TwitchEventSubChannelStatusScope statusScope,
    TwitchBotRuntimeStatusStore runtimeStatus,
    ITwitchEventSubChannelDiagnosticReporter diagnostics,
    TimeProvider timeProvider
) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, ActiveEventSubSubscription> subscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TwitchEventSubChannelStatus> states =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> authorizedChannels = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly HashSet<string> pendingStops = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly CancellationTokenSource sessionStop = new();
    private CancellationTokenSource? lifetime;
    private Task currentWork = Task.CompletedTask;
    private bool started;
    private bool disposed;

    internal IReadOnlyList<string> ActiveChannels
    {
        get
        {
            lock (gate)
                return subscriptions.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    internal void Start(
        IReadOnlyList<string> desiredChannels,
        CancellationToken cancellationToken
    )
    {
        var desired = TwitchChannelList.Normalize(desiredChannels);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
                throw new InvalidOperationException(
                    "EventSub channel recovery has already started for this session."
                );

            started = true;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                sessionStop.Token,
                cancellationToken
            );
        }

        statusScope.Activate();
        runtimeStatus.ActivateEventSubScope(statusScope.Id);
        lock (gate)
        {
            UpdateRuntimeStatusLocked();
            ScheduleLocked(token =>
                Task.WhenAll(
                    desired.Select(channel =>
                        RunImmediateAsync(
                            channel,
                            TwitchEventSubChannelReconciliationTarget.Present,
                            TwitchEventSubChannelRecoveryTrigger.Startup,
                            token
                        )
                    )
                )
            );
        }
    }

    internal void TriggerReconciliation(
        IReadOnlyList<string> desiredChannels,
        TwitchEventSubChannelRecoveryTrigger trigger
    )
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!started)
                throw new InvalidOperationException(
                    "EventSub channel recovery must start before reconciliation is triggered."
                );

            if (!currentWork.IsCompleted)
                return;

            currentWork.GetAwaiter().GetResult();
            var desired = TwitchChannelList.Normalize(desiredChannels);
            ScheduleLocked(token => RunReconciliationAsync(desired, trigger, token));
        }
    }

    internal async Task DrainAsync()
    {
        Task work;
        lock (gate)
            work = currentWork;

        await work;
    }

    public async ValueTask DisposeAsync()
    {
        Task work;
        CancellationTokenSource? linkedLifetime;
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            work = currentWork;
            linkedLifetime = lifetime;
        }

        Exception? failure = null;
        try
        {
            sessionStop.Cancel();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await work;
        }
        catch (OperationCanceledException) when (sessionStop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        try
        {
            runtimeStatus.DeactivateEventSubScope(statusScope.Id);
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        try
        {
            statusScope.Dispose();
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        try
        {
            linkedLifetime?.Dispose();
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        try
        {
            sessionStop.Dispose();
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void ScheduleLocked(Func<CancellationToken, Task> operation)
    {
        var token = lifetime?.Token
            ?? throw new InvalidOperationException(
                "EventSub channel recovery does not have a session lifetime."
            );
        currentWork = Task.Run(() => operation(token), CancellationToken.None);
    }

    private static Exception CombineCleanupFailures(
        Exception? previous,
        Exception current
    ) =>
        previous is null
            ? current
            : new AggregateException(
                "EventSub channel session cleanup failed.",
                previous,
                current
            );

    private async Task RunReconciliationAsync(
        IReadOnlyList<string> desiredChannels,
        TwitchEventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        string[] trackedChannels;
        lock (gate)
            trackedChannels = subscriptions.Keys.Union(states.Keys).ToArray();

        var desired = TwitchChannelList.Normalize(desiredChannels);
        var removed = trackedChannels
            .Except(desired, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await Task.WhenAll(
            desired
                .Select(channel =>
                    RunTriggeredAsync(
                        channel,
                        TwitchEventSubChannelReconciliationTarget.Present,
                        trigger,
                        cancellationToken
                    )
                )
                .Concat(
                    removed.Select(channel =>
                        RunTriggeredAsync(
                            channel,
                            TwitchEventSubChannelReconciliationTarget.Absent,
                            trigger,
                            cancellationToken
                        )
                    )
                )
        );
    }

    private async Task RunTriggeredAsync(
        string channel,
        TwitchEventSubChannelReconciliationTarget target,
        TwitchEventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        TwitchEventSubChannelStatus? state;
        lock (gate)
            states.TryGetValue(channel, out state);

        if (state is TwitchEventSubChannelStatus.Degraded degraded)
        {
            await RunRecoveryCycleAsync(
                channel,
                target,
                trigger,
                new TwitchEventSubChannelFailureDetails(
                    degraded.Phase,
                    degraded.Failure.Classification,
                    degraded.Failure.FailureType
                ),
                cancellationToken
            );
            return;
        }

        await RunImmediateAsync(
            channel,
            target,
            trigger,
            cancellationToken
        );
    }

    private async Task RunImmediateAsync(
        string channel,
        TwitchEventSubChannelReconciliationTarget target,
        TwitchEventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        var context = new TwitchEventSubChannelAttemptContext();
        try
        {
            await recovery.ExecuteAttemptAsync(
                token => ReconcileAsync(channel, target, context, token),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = TwitchEventSubChannelFailureClassifier.Classify(
                exception,
                context.Phase,
                cancellationToken
            );
            var isRecoverable = TwitchEventSubChannelFailureClassifier.IsRecoverable(
                failure.Classification
            );
            PublishDegraded(
                channel,
                trigger,
                attempt: 1,
                failure,
                isRecoverable
                    ? TwitchEventSubChannelNextAction.BeginRecoveryCycle
                    : TwitchEventSubChannelNextAction.RetryOnNextReconciliation
            );

            if (isRecoverable)
            {
                await RunRecoveryCycleAsync(
                    channel,
                    target,
                    trigger,
                    failure,
                    cancellationToken
                );
            }

            return;
        }

        PublishSuccess(channel, target, attempt: 1, trigger);
    }

    private async Task RunRecoveryCycleAsync(
        string channel,
        TwitchEventSubChannelReconciliationTarget target,
        TwitchEventSubChannelRecoveryTrigger trigger,
        TwitchEventSubChannelFailureDetails initialFailure,
        CancellationToken cancellationToken
    )
    {
        var attempt = 0;
        var latestFailure = initialFailure;
        var context = new TwitchEventSubChannelAttemptContext
        {
            Phase = initialFailure.Phase,
        };
        try
        {
            await recovery.ExecuteRecoveryAsync(
                async attemptToken =>
                {
                    checked
                    {
                        attempt++;
                    }

                    PublishRecovering(
                        channel,
                        trigger,
                        attempt,
                        latestFailure
                    );
                    try
                    {
                        await ReconcileAsync(channel, target, context, attemptToken);
                    }
                    catch (Exception exception)
                    {
                        latestFailure = TwitchEventSubChannelFailureClassifier.Classify(
                            exception,
                            context.Phase,
                            cancellationToken
                        );
                        throw;
                    }
                },
                cancellationToken
            );
        }
        catch (TwitchEventSubChannelStatusPublicationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            latestFailure = TwitchEventSubChannelFailureClassifier.Classify(
                exception,
                context.Phase,
                cancellationToken
            );
            PublishDegraded(
                channel,
                trigger,
                attempt,
                latestFailure,
                TwitchEventSubChannelNextAction.RetryOnNextReconciliation
            );
            return;
        }

        PublishSuccess(channel, target, attempt, trigger);
    }

    private ValueTask ReconcileAsync(
        string channel,
        TwitchEventSubChannelReconciliationTarget target,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    ) =>
        target switch
        {
            TwitchEventSubChannelReconciliationTarget.Present =>
                EnsurePresentAsync(channel, context, cancellationToken),
            TwitchEventSubChannelReconciliationTarget.Absent =>
                EnsureAbsentAsync(channel, context, cancellationToken),
            _ => throw new UnreachableException(
                "Unknown EventSub channel reconciliation target."
            ),
        };

    private async ValueTask EnsurePresentAsync(
        string channel,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        await CompletePendingStopAsync(channel, context, cancellationToken);
        var account = await RunPhaseAsync(
            context,
            TwitchEventSubChannelPhase.AccountResolution,
            token => operations.ResolveAccountAsync(channel, token),
            cancellationToken
        );
        lock (gate)
            authorizedChannels.Add(channel);

        ActiveEventSubSubscription? active;
        lock (gate)
            subscriptions.TryGetValue(channel, out active);

        if (
            active is not null
            && !active.BotLogin.Equals(account.Login, StringComparison.OrdinalIgnoreCase)
        )
        {
            await RunPhaseAsync(
                context,
                TwitchEventSubChannelPhase.Reconciliation,
                token => operations.DeleteSubscriptionAsync(active, token),
                cancellationToken
            );
            lock (gate)
            {
                subscriptions.Remove(channel);
                pendingStops.Add(channel);
            }
            await CompletePendingStopAsync(channel, context, cancellationToken);
            active = null;
        }

        if (active is null)
        {
            active = await RunPhaseAsync(
                context,
                TwitchEventSubChannelPhase.SubscriptionSetup,
                token =>
                    operations.CreateSubscriptionAsync(
                        channel,
                        account,
                        sessionId,
                        token
                    ),
                cancellationToken
            );
            lock (gate)
                subscriptions[channel] = active;
        }

        if (active.Readiness is TwitchEventSubSubscriptionReadiness.PendingSetup)
        {
            await RunPhaseAsync(
                context,
                TwitchEventSubChannelPhase.SubscriptionSetup,
                token => operations.CompleteSetupAsync(channel, token),
                cancellationToken
            );
            lock (gate)
                subscriptions[channel] = active with
                {
                    Readiness = TwitchEventSubSubscriptionReadiness.Ready,
                };
        }

        context.Phase = TwitchEventSubChannelPhase.Reconciliation;
    }

    private async ValueTask EnsureAbsentAsync(
        string channel,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        await CompletePendingStopAsync(channel, context, cancellationToken);
        ActiveEventSubSubscription? active;
        lock (gate)
            subscriptions.TryGetValue(channel, out active);

        if (active is not null)
        {
            await RunPhaseAsync(
                context,
                TwitchEventSubChannelPhase.Reconciliation,
                token => operations.DeleteSubscriptionAsync(active, token),
                cancellationToken
            );
            lock (gate)
            {
                subscriptions.Remove(channel);
                pendingStops.Add(channel);
            }
            await CompletePendingStopAsync(channel, context, cancellationToken);
        }

        lock (gate)
            authorizedChannels.Remove(channel);
        context.Phase = TwitchEventSubChannelPhase.Reconciliation;
    }

    private async ValueTask CompletePendingStopAsync(
        string channel,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        lock (gate)
        {
            if (!pendingStops.Contains(channel))
                return;
        }

        await RunPhaseAsync(
            context,
            TwitchEventSubChannelPhase.Reconciliation,
            token => operations.CompleteStopAsync(channel, token),
            cancellationToken
        );
        lock (gate)
            pendingStops.Remove(channel);
    }

    private static async ValueTask<T> RunPhaseAsync<T>(
        TwitchEventSubChannelAttemptContext context,
        TwitchEventSubChannelPhase phase,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken
    )
    {
        context.Phase = phase;
        try
        {
            return await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TwitchEventSubChannelOperationException(phase, exception);
        }
    }

    private static async ValueTask RunPhaseAsync(
        TwitchEventSubChannelAttemptContext context,
        TwitchEventSubChannelPhase phase,
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken
    )
    {
        context.Phase = phase;
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TwitchEventSubChannelOperationException(phase, exception);
        }
    }

    private void PublishSuccess(
        string channel,
        TwitchEventSubChannelReconciliationTarget target,
        int attempt,
        TwitchEventSubChannelRecoveryTrigger trigger
    )
    {
        switch (target)
        {
            case TwitchEventSubChannelReconciliationTarget.Present:
                Publish(
                    new TwitchEventSubChannelStatus.Healthy
                    {
                        Channel = channel,
                        Phase = TwitchEventSubChannelPhase.Reconciliation,
                        Attempt = attempt,
                        ChangedAt = timeProvider.GetUtcNow(),
                        Trigger = trigger,
                    }
                );
                return;
            case TwitchEventSubChannelReconciliationTarget.Absent:
                lock (gate)
                {
                    states.Remove(channel);
                    statusScope.Remove(channel);
                    UpdateRuntimeStatusLocked();
                }
                return;
            default:
                throw new UnreachableException(
                    "Unknown EventSub channel reconciliation target."
                );
        }
    }

    private void PublishRecovering(
        string channel,
        TwitchEventSubChannelRecoveryTrigger trigger,
        int attempt,
        TwitchEventSubChannelFailureDetails failure
    ) =>
        Publish(
            new TwitchEventSubChannelStatus.Recovering
            {
                Channel = channel,
                Phase = failure.Phase,
                Attempt = attempt,
                ChangedAt = timeProvider.GetUtcNow(),
                Trigger = trigger,
                Failure = failure.ToPublicFailure(),
                NextAction = TwitchEventSubChannelNextAction.ContinueRecoveryCycle,
            }
        );

    private void PublishDegraded(
        string channel,
        TwitchEventSubChannelRecoveryTrigger trigger,
        int attempt,
        TwitchEventSubChannelFailureDetails failure,
        TwitchEventSubChannelNextAction nextAction
    )
    {
        if (failure.Phase is TwitchEventSubChannelPhase.AccountResolution)
        {
            lock (gate)
                authorizedChannels.Remove(channel);
        }

        Publish(
            new TwitchEventSubChannelStatus.Degraded
            {
                Channel = channel,
                Phase = failure.Phase,
                Attempt = attempt,
                ChangedAt = timeProvider.GetUtcNow(),
                Trigger = trigger,
                Failure = failure.ToPublicFailure(),
                NextAction = nextAction,
            }
        );
    }

    private void Publish(TwitchEventSubChannelStatus state)
    {
        try
        {
            lock (gate)
            {
                states[state.Channel] = state;
                statusScope.Set(state);
                UpdateRuntimeStatusLocked();
            }

            diagnostics.Report(state);
        }
        catch (Exception exception)
        {
            throw new TwitchEventSubChannelStatusPublicationException(exception);
        }
    }

    private void UpdateRuntimeStatusLocked()
    {
        var healthyChannels = states.Values
            .OfType<TwitchEventSubChannelStatus.Healthy>()
            .Select(state => state.Channel)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        runtimeStatus.SetEventSubStatus(
            statusScope.Id,
            authorizedChannels.Count > 0,
            healthyChannels
        );
    }

    private sealed class TwitchEventSubChannelAttemptContext
    {
        internal TwitchEventSubChannelPhase Phase { get; set; } =
            TwitchEventSubChannelPhase.AccountResolution;
    }
}

internal enum TwitchEventSubChannelReconciliationTarget
{
    Present,
    Absent,
}

internal sealed class TwitchEventSubChannelStatusPublicationException(
    Exception innerException
) : Exception("EventSub channel status publication failed.", innerException);

internal enum TwitchEventSubSubscriptionReadiness
{
    PendingSetup,
    Ready,
}

internal sealed record ActiveEventSubSubscription
{
    internal required string Channel { get; init; }

    internal required string SubscriptionId { get; init; }

    internal required string BotLogin { get; init; }

    internal required string AccessToken { get; init; }

    internal required TwitchEventSubSubscriptionReadiness Readiness { get; init; }
}
