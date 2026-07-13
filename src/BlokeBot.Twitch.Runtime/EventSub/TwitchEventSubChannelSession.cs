using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace BlokeBot.Twitch.Runtime;

internal interface ITwitchEventSubChannelOperations
{
    ValueTask<TwitchBotAccount> ResolveAccountAsync(
        string channel,
        CancellationToken cancellationToken
    );

    ValueTask<TwitchEventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        TwitchBotAccount account,
        string sessionId,
        CancellationToken cancellationToken
    );

    ValueTask<TwitchEventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
        string channel,
        CancellationToken cancellationToken
    );

    ValueTask NotifyChannelStartedAsync(string channel, CancellationToken cancellationToken);

    ValueTask<TwitchEventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken cancellationToken
    );

    ValueTask CompleteStopAsync(string channel, CancellationToken cancellationToken);
}

internal abstract record TwitchEventSubSubscriptionSetupOutcome
{
    private protected TwitchEventSubSubscriptionSetupOutcome() { }

    internal sealed record Created(ActiveEventSubSubscription Subscription)
        : TwitchEventSubSubscriptionSetupOutcome;

    internal sealed record MissingChannel : TwitchEventSubSubscriptionSetupOutcome;

    internal sealed record MissingBot : TwitchEventSubSubscriptionSetupOutcome;
}

internal abstract record TwitchEventSubChannelReconciliationOutcome
{
    private TwitchEventSubChannelReconciliationOutcome() { }

    internal TResult Match<TResult>(
        Func<Completed, TResult> completed,
        Func<MissingChannel, TResult> missingChannel,
        Func<MissingBot, TResult> missingBot,
        Func<StartupMessageRejected, TResult> startupMessageRejected,
        Func<UnresolvedDeletion, TResult> unresolvedDeletion
    )
    {
        return this switch
        {
            Completed outcome => completed(outcome),
            MissingChannel outcome => missingChannel(outcome),
            MissingBot outcome => missingBot(outcome),
            StartupMessageRejected outcome => startupMessageRejected(outcome),
            UnresolvedDeletion outcome => unresolvedDeletion(outcome),
            _ => throw new UnreachableException("Unknown EventSub channel reconciliation outcome."),
        };
    }

    internal sealed record Completed : TwitchEventSubChannelReconciliationOutcome;

    internal sealed record MissingChannel : TwitchEventSubChannelReconciliationOutcome;

    internal sealed record MissingBot : TwitchEventSubChannelReconciliationOutcome;

    internal sealed record StartupMessageRejected : TwitchEventSubChannelReconciliationOutcome;

    internal sealed record UnresolvedDeletion : TwitchEventSubChannelReconciliationOutcome
    {
        internal required TwitchEventSubChannelFailureDetails Failure { get; init; }

        public override string ToString()
        {
            return nameof(UnresolvedDeletion);
        }
    }
}

internal abstract record TwitchEventSubStartupDeliveryOutcome
{
    private TwitchEventSubStartupDeliveryOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Completed, TResult> completed,
        Func<Rejected, TResult> rejected
    );

    internal sealed record Completed : TwitchEventSubStartupDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<Rejected, TResult> rejected
        )
        {
            return completed(this);
        }
    }

    internal sealed record Rejected : TwitchEventSubStartupDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<Rejected, TResult> rejected
        )
        {
            return rejected(this);
        }
    }
}

internal sealed class TwitchEventSubChannelOperations(
    TwitchBotSettings settings,
    ITwitchBotAccountProvider accounts,
    ChatIdentityResolver identities,
    EventSubClient eventSub,
    ITwitchChatMessageSender sender,
    ITwitchBotChannelLifecycleNotifier lifecycle
) : ITwitchEventSubChannelOperations
{
    public ValueTask<TwitchBotAccount> ResolveAccountAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        return accounts.GetBotAccountAsync(channel, cancellationToken);
    }

    public async ValueTask<TwitchEventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        TwitchBotAccount account,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        var resolution = await identities.ResolveAsync(
            channel,
            account.Login,
            account.AccessToken,
            cancellationToken
        );
        return await resolution.Match(
            resolved =>
                CreateResolvedSubscriptionAsync(
                    channel,
                    account,
                    sessionId,
                    resolved,
                    cancellationToken
                ),
            static _ =>
                ValueTask.FromResult<TwitchEventSubSubscriptionSetupOutcome>(
                    new TwitchEventSubSubscriptionSetupOutcome.MissingChannel()
                ),
            static _ =>
                ValueTask.FromResult<TwitchEventSubSubscriptionSetupOutcome>(
                    new TwitchEventSubSubscriptionSetupOutcome.MissingBot()
                )
        );
    }

    private async ValueTask<TwitchEventSubSubscriptionSetupOutcome> CreateResolvedSubscriptionAsync(
        string channel,
        TwitchBotAccount account,
        string sessionId,
        ChatIdentityResolution.Resolved resolved,
        CancellationToken cancellationToken
    )
    {
        return new TwitchEventSubSubscriptionSetupOutcome.Created(
            new ActiveEventSubSubscription
            {
                Channel = channel,
                SubscriptionId = await eventSub.CreateChatMessageSubscriptionAsync(
                    new HelixRequestContext(settings.Identity.ClientId, account.AccessToken),
                    resolved.BroadcasterId,
                    resolved.BotUserId,
                    sessionId,
                    cancellationToken
                ),
                BotLogin = account.Login,
                AccessToken = account.AccessToken,
                Readiness = TwitchEventSubSubscriptionReadiness.PendingStartupDelivery,
            }
        );
    }

    public async ValueTask<TwitchEventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(settings.StartupMessage))
        {
            return new TwitchEventSubStartupDeliveryOutcome.Completed();
        }

        var outcome = await sender.SendAsync(
            channel,
            settings.StartupMessage,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        return outcome.Match<TwitchEventSubStartupDeliveryOutcome>(
            static _ => new TwitchEventSubStartupDeliveryOutcome.Completed(),
            static _ => new TwitchEventSubStartupDeliveryOutcome.Rejected()
        );
    }

    public ValueTask NotifyChannelStartedAsync(string channel, CancellationToken cancellationToken)
    {
        return new(lifecycle.ChannelStartedAsync(channel, cancellationToken));
    }

    public async ValueTask<TwitchEventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await eventSub.DeleteSubscriptionAsync(
                new HelixRequestContext(settings.Identity.ClientId, subscription.AccessToken),
                subscription.SubscriptionId,
                cancellationToken
            );
            return new TwitchEventSubSubscriptionDeletionOutcome.Deleted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new TwitchEventSubSubscriptionDeletionOutcome.Unresolved
            {
                Failure = TwitchEventSubChannelFailureClassifier.Classify(
                    exception,
                    TwitchEventSubChannelPhase.SubscriptionDeletion,
                    cancellationToken
                ),
            };
        }
    }

    public ValueTask CompleteStopAsync(string channel, CancellationToken cancellationToken)
    {
        return new(lifecycle.ChannelStoppedAsync(channel, cancellationToken));
    }
}

internal sealed class TwitchEventSubChannelSessionFactory(
    ITwitchEventSubChannelOperations operations,
    TwitchEventSubChannelRecoveryPipeline recovery,
    TwitchEventSubSubscriptionReconciliationStore pendingDeletions,
    TwitchEventSubChannelStatusStore channelStatus,
    TwitchBotRuntimeStatusStore runtimeStatus,
    ITwitchEventSubChannelDiagnosticReporter diagnostics,
    TimeProvider timeProvider
)
{
    internal bool HasPendingReconciliation => pendingDeletions.HasPendingReconciliation;

    internal TwitchEventSubChannelSession Create(string sessionId)
    {
        return new(
            sessionId,
            operations,
            recovery,
            pendingDeletions,
            channelStatus.CreateScope(),
            runtimeStatus,
            diagnostics,
            timeProvider
        );
    }
}

internal sealed class TwitchEventSubChannelSession(
    string sessionId,
    ITwitchEventSubChannelOperations operations,
    TwitchEventSubChannelRecoveryPipeline recovery,
    TwitchEventSubSubscriptionReconciliationStore pendingDeletions,
    TwitchEventSubChannelStatusStore.TwitchEventSubChannelStatusScope statusScope,
    TwitchBotRuntimeStatusStore runtimeStatus,
    ITwitchEventSubChannelDiagnosticReporter diagnostics,
    TimeProvider timeProvider
) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ActiveEventSubSubscription> _subscriptions = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, TwitchEventSubChannelStatus> _states = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, TwitchEventSubChannelFailureContext> _failures = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly HashSet<string> _authorizedChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _sessionStop = new();
    private CancellationTokenSource? _lifetime;
    private Task _currentWork = Task.CompletedTask;
    private bool _started;
    private bool _disposed;

    internal IReadOnlyList<string> ActiveChannels
    {
        get
        {
            string[] active;
            lock (_gate)
            {
                active = _subscriptions.Keys.ToArray();
            }

            return active
                .Union(pendingDeletions.PendingDeletionChannels, StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    internal void Start(IReadOnlyList<string> desiredChannels, CancellationToken cancellationToken)
    {
        var desired = TwitchChannelList.Normalize(desiredChannels);
        var desiredSet = desired.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var initial = desired
            .Union(pendingDeletions.ReconciliationChannels, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException(
                    "EventSub channel recovery has already started for this session."
                );
            }

            _started = true;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                _sessionStop.Token,
                cancellationToken
            );
        }

        statusScope.Activate();
        runtimeStatus.ActivateEventSubScope(statusScope.Id);
        lock (_gate)
        {
            UpdateRuntimeStatusLocked();
            ScheduleLocked(token =>
                Task.WhenAll(
                    initial.Select(channel =>
                        RunImmediateAsync(
                            channel,
                            desiredSet.Contains(channel)
                                ? TwitchEventSubChannelReconciliationTarget.Present
                                : TwitchEventSubChannelReconciliationTarget.Absent,
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
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_started)
            {
                throw new InvalidOperationException(
                    "EventSub channel recovery must start before reconciliation is triggered."
                );
            }

            if (!_currentWork.IsCompleted)
            {
                return;
            }

            _currentWork.GetAwaiter().GetResult();
            var desired = TwitchChannelList.Normalize(desiredChannels);
            ScheduleLocked(token => RunReconciliationAsync(desired, trigger, token));
        }
    }

    internal async Task DrainAsync()
    {
        Task work;
        lock (_gate)
        {
            work = _currentWork;
        }

        await work;
    }

    public async ValueTask DisposeAsync()
    {
        Task work;
        CancellationTokenSource? linkedLifetime;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            work = _currentWork;
            linkedLifetime = _lifetime;
        }

        Exception? failure = null;
        try
        {
            _sessionStop.Cancel();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await work;
        }
        catch (OperationCanceledException) when (_sessionStop.IsCancellationRequested) { }
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
            _sessionStop.Dispose();
        }
        catch (Exception exception)
        {
            failure = CombineCleanupFailures(failure, exception);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void ScheduleLocked(Func<CancellationToken, Task> operation)
    {
        var token =
            _lifetime?.Token
            ?? throw new InvalidOperationException(
                "EventSub channel recovery does not have a session lifetime."
            );
        _currentWork = Task.Run(() => operation(token), CancellationToken.None);
    }

    private static Exception CombineCleanupFailures(Exception? previous, Exception current)
    {
        return previous is null
            ? current
            : new AggregateException("EventSub channel session cleanup failed.", previous, current);
    }

    private async Task RunReconciliationAsync(
        IReadOnlyList<string> desiredChannels,
        TwitchEventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        string[] trackedChannels;
        lock (_gate)
        {
            trackedChannels = _subscriptions.Keys.Union(_states.Keys).ToArray();
        }

        trackedChannels = trackedChannels
            .Union(pendingDeletions.ReconciliationChannels, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var desired = TwitchChannelList.Normalize(desiredChannels);
        var removed = trackedChannels.Except(desired, StringComparer.OrdinalIgnoreCase).ToArray();
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
        lock (_gate)
        {
            _states.TryGetValue(channel, out state);
        }

        if (state is TwitchEventSubChannelStatus.Degraded degraded)
        {
            if (
                target is TwitchEventSubChannelReconciliationTarget.Present
                && degraded.NextAction is TwitchEventSubChannelNextAction.NoFurtherAction
            )
            {
                return;
            }

            await RunRecoveryCycleAsync(
                channel,
                target,
                trigger,
                GetFailureContext(channel),
                cancellationToken
            );
            return;
        }

        await RunImmediateAsync(channel, target, trigger, cancellationToken);
    }

    private async Task RunImmediateAsync(
        string channel,
        TwitchEventSubChannelReconciliationTarget target,
        TwitchEventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        var context = new TwitchEventSubChannelAttemptContext();
        TwitchEventSubChannelReconciliationOutcome outcome;
        try
        {
            outcome = await recovery.ExecuteAttemptAsync(
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
            var failureDetails = TwitchEventSubChannelFailureClassifier.Classify(
                exception,
                context.Phase,
                cancellationToken
            );
            var failure = new TwitchEventSubChannelFailureContext.ClassifiedException(
                failureDetails
            );
            RetainPendingDeletionFailure(channel, failureDetails);
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
                await RunRecoveryCycleAsync(channel, target, trigger, failure, cancellationToken);
            }

            return;
        }

        await outcome.Match(
            PublishOutcomeAsync,
            PublishOutcomeAsync,
            PublishOutcomeAsync,
            PublishOutcomeAsync,
            HandleUnresolvedDeletionAsync
        );
        return;

        ValueTask PublishOutcomeAsync(TwitchEventSubChannelReconciliationOutcome result)
        {
            PublishReconciliationOutcome(channel, target, trigger, attempt: 1, result);
            return ValueTask.CompletedTask;
        }

        async ValueTask HandleUnresolvedDeletionAsync(
            TwitchEventSubChannelReconciliationOutcome.UnresolvedDeletion unresolved
        )
        {
            var failure = new TwitchEventSubChannelFailureContext.ClassifiedException(
                unresolved.Failure
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
                await RunRecoveryCycleAsync(channel, target, trigger, failure, cancellationToken);
            }
        }
    }

    private TwitchEventSubChannelFailureContext GetFailureContext(string channel)
    {
        lock (_gate)
        {
            return _failures.TryGetValue(channel, out var failure)
                ? failure
                : throw new UnreachableException(
                    "A failed EventSub channel state has no internal failure context."
                );
        }
    }

    private void RetainPendingDeletionFailure(
        string channel,
        TwitchEventSubChannelFailureDetails failure
    )
    {
        if (
            failure.Phase is not TwitchEventSubChannelPhase.SubscriptionDeletion
            || failure.Classification is TwitchEventSubChannelFailureClassification.Cancellation
        )
        {
            return;
        }

        if (!pendingDeletions.TryGet(channel, out var pending))
        {
            throw new UnreachableException(
                "An unresolved EventSub deletion has no pending local evidence."
            );
        }

        pendingDeletions.RetainUnresolved(pending.Subscription, failure);
    }

    private async Task RunRecoveryCycleAsync(
        string channel,
        TwitchEventSubChannelReconciliationTarget target,
        TwitchEventSubChannelRecoveryTrigger trigger,
        TwitchEventSubChannelFailureContext initialFailure,
        CancellationToken cancellationToken
    )
    {
        var attempt = 0;
        var latestFailure = initialFailure;
        var context = new TwitchEventSubChannelAttemptContext { Phase = initialFailure.Phase };
        TwitchEventSubChannelReconciliationOutcome outcome;
        try
        {
            outcome = await recovery.ExecuteRecoveryAsync(
                async attemptToken =>
                {
                    checked
                    {
                        attempt++;
                    }

                    PublishRecovering(channel, trigger, attempt, latestFailure);
                    try
                    {
                        var outcome = await ReconcileAsync(channel, target, context, attemptToken);
                        if (
                            outcome
                            is TwitchEventSubChannelReconciliationOutcome.UnresolvedDeletion unresolved
                        )
                        {
                            latestFailure =
                                new TwitchEventSubChannelFailureContext.ClassifiedException(
                                    unresolved.Failure
                                );
                        }

                        return outcome;
                    }
                    catch (Exception exception)
                    {
                        var failureDetails = TwitchEventSubChannelFailureClassifier.Classify(
                            exception,
                            context.Phase,
                            cancellationToken
                        );
                        latestFailure = new TwitchEventSubChannelFailureContext.ClassifiedException(
                            failureDetails
                        );
                        RetainPendingDeletionFailure(channel, failureDetails);
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
            var failureDetails = TwitchEventSubChannelFailureClassifier.Classify(
                exception,
                context.Phase,
                cancellationToken
            );
            latestFailure = new TwitchEventSubChannelFailureContext.ClassifiedException(
                failureDetails
            );
            RetainPendingDeletionFailure(channel, failureDetails);
            PublishDegraded(
                channel,
                trigger,
                attempt,
                latestFailure,
                TwitchEventSubChannelNextAction.RetryOnNextReconciliation
            );
            return;
        }

        PublishReconciliationOutcome(channel, target, trigger, attempt, outcome);
    }

    private ValueTask<TwitchEventSubChannelReconciliationOutcome> ReconcileAsync(
        string channel,
        TwitchEventSubChannelReconciliationTarget target,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        return target switch
        {
            TwitchEventSubChannelReconciliationTarget.Present => EnsurePresentAsync(
                channel,
                context,
                cancellationToken
            ),
            TwitchEventSubChannelReconciliationTarget.Absent => EnsureAbsentAsync(
                channel,
                context,
                cancellationToken
            ),
            _ => throw new UnreachableException("Unknown EventSub channel reconciliation target."),
        };
    }

    private async ValueTask<TwitchEventSubChannelReconciliationOutcome> EnsurePresentAsync(
        string channel,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var pendingDeletion = await ReconcilePendingDeletionAsync(
            channel,
            context,
            cancellationToken
        );
        switch (pendingDeletion)
        {
            case TwitchEventSubChannelReconciliationOutcome.Completed:
                break;
            case TwitchEventSubChannelReconciliationOutcome.UnresolvedDeletion:
                return pendingDeletion;
            default:
                throw new UnreachableException(
                    "Pending EventSub deletion produced a non-deletion reconciliation outcome."
                );
        }

        await CompletePendingStopAsync(channel, context, cancellationToken);
        var account = await RunPhaseAsync(
            context,
            TwitchEventSubChannelPhase.AccountResolution,
            token => operations.ResolveAccountAsync(channel, token),
            cancellationToken
        );
        lock (_gate)
        {
            _authorizedChannels.Add(channel);
        }

        ActiveEventSubSubscription? active;
        lock (_gate)
        {
            _subscriptions.TryGetValue(channel, out active);
        }

        if (
            active is not null
            && !active.BotLogin.Equals(account.Login, StringComparison.OrdinalIgnoreCase)
        )
        {
            var deletion = await ReconcileSubscriptionDeletionAsync(
                active,
                context,
                cancellationToken
            );
            switch (deletion)
            {
                case TwitchEventSubChannelReconciliationOutcome.Completed:
                    break;
                case TwitchEventSubChannelReconciliationOutcome.UnresolvedDeletion:
                    return deletion;
                default:
                    throw new UnreachableException(
                        "EventSub subscription deletion produced a non-deletion reconciliation outcome."
                    );
            }

            await CompletePendingStopAsync(channel, context, cancellationToken);
            active = null;
        }

        if (active is null)
        {
            var setup = await RunPhaseAsync(
                context,
                TwitchEventSubChannelPhase.SubscriptionSetup,
                token => operations.CreateSubscriptionAsync(channel, account, sessionId, token),
                cancellationToken
            );
            switch (setup)
            {
                case TwitchEventSubSubscriptionSetupOutcome.Created created:
                    active = created.Subscription;
                    break;
                case TwitchEventSubSubscriptionSetupOutcome.MissingChannel:
                    return new TwitchEventSubChannelReconciliationOutcome.MissingChannel();
                case TwitchEventSubSubscriptionSetupOutcome.MissingBot:
                    return new TwitchEventSubChannelReconciliationOutcome.MissingBot();
                default:
                    throw new UnreachableException("Unknown EventSub subscription setup outcome.");
            }

            lock (_gate)
            {
                _subscriptions[channel] = active;
            }
        }

        switch (active.Readiness)
        {
            case TwitchEventSubSubscriptionReadiness.PendingStartupDelivery:
                var startupDelivery = await RunPhaseAsync(
                    context,
                    TwitchEventSubChannelPhase.SubscriptionSetup,
                    token => operations.DeliverStartupMessageAsync(channel, token),
                    cancellationToken
                );
                if (!startupDelivery.Match(static _ => true, static _ => false))
                {
                    return new TwitchEventSubChannelReconciliationOutcome.StartupMessageRejected();
                }

                active = active with
                {
                    Readiness = TwitchEventSubSubscriptionReadiness.PendingLifecycleStart,
                };
                lock (_gate)
                {
                    _subscriptions[channel] = active;
                }

                goto case TwitchEventSubSubscriptionReadiness.PendingLifecycleStart;
            case TwitchEventSubSubscriptionReadiness.PendingLifecycleStart:
                await RunPhaseAsync(
                    context,
                    TwitchEventSubChannelPhase.SubscriptionSetup,
                    token => operations.NotifyChannelStartedAsync(channel, token),
                    cancellationToken
                );
                active = active with { Readiness = TwitchEventSubSubscriptionReadiness.Ready };
                lock (_gate)
                {
                    _subscriptions[channel] = active;
                }

                break;
            case TwitchEventSubSubscriptionReadiness.Ready:
                break;
            default:
                throw new UnreachableException("Unknown EventSub subscription setup stage.");
        }

        context.Phase = TwitchEventSubChannelPhase.Reconciliation;
        return new TwitchEventSubChannelReconciliationOutcome.Completed();
    }

    private async ValueTask<TwitchEventSubChannelReconciliationOutcome> EnsureAbsentAsync(
        string channel,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var pendingDeletion = await ReconcilePendingDeletionAsync(
            channel,
            context,
            cancellationToken
        );
        switch (pendingDeletion)
        {
            case TwitchEventSubChannelReconciliationOutcome.Completed:
                break;
            case TwitchEventSubChannelReconciliationOutcome.UnresolvedDeletion:
                return pendingDeletion;
            default:
                throw new UnreachableException(
                    "Pending EventSub deletion produced a non-deletion reconciliation outcome."
                );
        }

        await CompletePendingStopAsync(channel, context, cancellationToken);
        ActiveEventSubSubscription? active;
        lock (_gate)
        {
            _subscriptions.TryGetValue(channel, out active);
        }

        if (active is not null)
        {
            var deletion = await ReconcileSubscriptionDeletionAsync(
                active,
                context,
                cancellationToken
            );
            switch (deletion)
            {
                case TwitchEventSubChannelReconciliationOutcome.Completed:
                    break;
                case TwitchEventSubChannelReconciliationOutcome.UnresolvedDeletion:
                    return deletion;
                default:
                    throw new UnreachableException(
                        "EventSub subscription deletion produced a non-deletion reconciliation outcome."
                    );
            }

            await CompletePendingStopAsync(channel, context, cancellationToken);
        }

        lock (_gate)
        {
            _authorizedChannels.Remove(channel);
        }

        context.Phase = TwitchEventSubChannelPhase.Reconciliation;
        return new TwitchEventSubChannelReconciliationOutcome.Completed();
    }

    private async ValueTask<TwitchEventSubChannelReconciliationOutcome> ReconcilePendingDeletionAsync(
        string channel,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        if (!pendingDeletions.TryGet(channel, out var pending))
        {
            return new TwitchEventSubChannelReconciliationOutcome.Completed();
        }

        return await ReconcileSubscriptionDeletionAsync(
            pending.Subscription,
            context,
            cancellationToken
        );
    }

    private async ValueTask<TwitchEventSubChannelReconciliationOutcome> ReconcileSubscriptionDeletionAsync(
        ActiveEventSubSubscription subscription,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        pendingDeletions.Begin(subscription);
        var outcome = await RunPhaseAsync(
            context,
            TwitchEventSubChannelPhase.SubscriptionDeletion,
            token => operations.DeleteSubscriptionAsync(subscription, token),
            cancellationToken
        );
        switch (outcome)
        {
            case TwitchEventSubSubscriptionDeletionOutcome.Deleted:
                lock (_gate)
                {
                    if (
                        _subscriptions.TryGetValue(subscription.Channel, out var active)
                        && !active.SubscriptionId.Equals(
                            subscription.SubscriptionId,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        throw new UnreachableException(
                            "An EventSub subscription changed while its deletion was pending."
                        );
                    }

                    pendingDeletions.ConfirmDeleted(subscription);
                    _subscriptions.Remove(subscription.Channel);
                }
                return new TwitchEventSubChannelReconciliationOutcome.Completed();
            case TwitchEventSubSubscriptionDeletionOutcome.Unresolved unresolved:
                pendingDeletions.RetainUnresolved(subscription, unresolved.Failure);
                return new TwitchEventSubChannelReconciliationOutcome.UnresolvedDeletion
                {
                    Failure = unresolved.Failure,
                };
            default:
                throw new UnreachableException("Unknown EventSub subscription-deletion outcome.");
        }
    }

    private async ValueTask CompletePendingStopAsync(
        string channel,
        TwitchEventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        if (!pendingDeletions.HasPendingStop(channel))
        {
            return;
        }

        await RunPhaseAsync(
            context,
            TwitchEventSubChannelPhase.Reconciliation,
            token => operations.CompleteStopAsync(channel, token),
            cancellationToken
        );
        pendingDeletions.ConfirmStopped(channel);
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

    private void PublishReconciliationOutcome(
        string channel,
        TwitchEventSubChannelReconciliationTarget target,
        TwitchEventSubChannelRecoveryTrigger trigger,
        int attempt,
        TwitchEventSubChannelReconciliationOutcome outcome
    )
    {
        outcome
            .Match<Action>(
                _ => () => PublishSuccess(channel, target, attempt, trigger),
                _ =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new TwitchEventSubChannelFailureContext.MissingChannel(),
                            TwitchEventSubChannelNextAction.RetryOnNextReconciliation
                        ),
                _ =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new TwitchEventSubChannelFailureContext.MissingBot(),
                            TwitchEventSubChannelNextAction.RetryOnNextReconciliation
                        ),
                _ =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new TwitchEventSubChannelFailureContext.StartupMessageRejected(),
                            TwitchEventSubChannelNextAction.NoFurtherAction
                        ),
                unresolved =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new TwitchEventSubChannelFailureContext.ClassifiedException(
                                unresolved.Failure
                            ),
                            TwitchEventSubChannelNextAction.RetryOnNextReconciliation
                        )
            )
            .Invoke();
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
                    new TwitchEventSubChannelDiagnosticReport.Healthy
                    {
                        ChannelStatus = new TwitchEventSubChannelStatus.Healthy
                        {
                            Channel = channel,
                            Phase = TwitchEventSubChannelPhase.Reconciliation,
                            Attempt = attempt,
                            ChangedAt = timeProvider.GetUtcNow(),
                            Trigger = trigger,
                        },
                    }
                );
                return;
            case TwitchEventSubChannelReconciliationTarget.Absent:
                lock (_gate)
                {
                    _states.Remove(channel);
                    _failures.Remove(channel);
                    statusScope.Remove(channel);
                    UpdateRuntimeStatusLocked();
                }
                return;
            default:
                throw new UnreachableException("Unknown EventSub channel reconciliation target.");
        }
    }

    private void PublishRecovering(
        string channel,
        TwitchEventSubChannelRecoveryTrigger trigger,
        int attempt,
        TwitchEventSubChannelFailureContext failure
    )
    {
        Publish(
            new TwitchEventSubChannelDiagnosticReport.Recovering
            {
                ChannelStatus = new TwitchEventSubChannelStatus.Recovering
                {
                    Channel = channel,
                    Phase = failure.Phase,
                    Attempt = attempt,
                    ChangedAt = timeProvider.GetUtcNow(),
                    Trigger = trigger,
                    Failure = failure.ToPublicFailure(),
                    NextAction = TwitchEventSubChannelNextAction.ContinueRecoveryCycle,
                },
                Failure = failure,
            }
        );
    }

    private void PublishDegraded(
        string channel,
        TwitchEventSubChannelRecoveryTrigger trigger,
        int attempt,
        TwitchEventSubChannelFailureContext failure,
        TwitchEventSubChannelNextAction nextAction
    )
    {
        if (failure.Phase is TwitchEventSubChannelPhase.AccountResolution)
        {
            lock (_gate)
            {
                _authorizedChannels.Remove(channel);
            }
        }

        Publish(
            new TwitchEventSubChannelDiagnosticReport.Degraded
            {
                ChannelStatus = new TwitchEventSubChannelStatus.Degraded
                {
                    Channel = channel,
                    Phase = failure.Phase,
                    Attempt = attempt,
                    ChangedAt = timeProvider.GetUtcNow(),
                    Trigger = trigger,
                    Failure = failure.ToPublicFailure(),
                    NextAction = nextAction,
                },
                Failure = failure,
            }
        );
    }

    private void Publish(TwitchEventSubChannelDiagnosticReport report)
    {
        try
        {
            var state = report.Status;
            lock (_gate)
            {
                _states[state.Channel] = state;
                switch (report)
                {
                    case TwitchEventSubChannelDiagnosticReport.Healthy:
                        _failures.Remove(state.Channel);
                        break;
                    case TwitchEventSubChannelDiagnosticReport.Recovering recovering:
                        _failures[state.Channel] = recovering.Failure;
                        break;
                    case TwitchEventSubChannelDiagnosticReport.Degraded degraded:
                        _failures[state.Channel] = degraded.Failure;
                        break;
                    default:
                        throw new UnreachableException(
                            "Unknown EventSub channel diagnostic report."
                        );
                }

                statusScope.Set(state);
                UpdateRuntimeStatusLocked();
            }

            diagnostics.Report(report);
        }
        catch (Exception exception)
        {
            throw new TwitchEventSubChannelStatusPublicationException(exception);
        }
    }

    private void UpdateRuntimeStatusLocked()
    {
        var healthyChannels = _states
            .Values.OfType<TwitchEventSubChannelStatus.Healthy>()
            .Select(state => state.Channel)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        runtimeStatus.SetEventSubStatus(
            statusScope.Id,
            _authorizedChannels.Count > 0,
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

internal sealed class TwitchEventSubChannelStatusPublicationException(Exception innerException)
    : Exception("EventSub channel status publication failed.", innerException);

internal enum TwitchEventSubSubscriptionReadiness
{
    PendingStartupDelivery,
    PendingLifecycleStart,
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
