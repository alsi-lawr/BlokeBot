using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace BlokeBot.Twitch.Runtime;

internal interface IEventSubChannelOperations
{
    ValueTask<BotAccount> ResolveAccountAsync(string channel, CancellationToken cancellationToken);

    ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        BotAccount account,
        string sessionId,
        CancellationToken cancellationToken
    );

    ValueTask<EventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
        string channel,
        CancellationToken cancellationToken
    );

    ValueTask NotifyChannelStartedAsync(string channel, CancellationToken cancellationToken);

    ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken cancellationToken
    );

    ValueTask CompleteStopAsync(string channel, CancellationToken cancellationToken);
}

internal abstract record EventSubSubscriptionSetupOutcome
{
    private protected EventSubSubscriptionSetupOutcome() { }

    internal sealed record Created(ActiveEventSubSubscription Subscription)
        : EventSubSubscriptionSetupOutcome;

    internal sealed record MissingChannel : EventSubSubscriptionSetupOutcome;

    internal sealed record MissingBot : EventSubSubscriptionSetupOutcome;
}

internal abstract record EventSubChannelReconciliationOutcome
{
    private EventSubChannelReconciliationOutcome() { }

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

    internal sealed record Completed : EventSubChannelReconciliationOutcome;

    internal sealed record MissingChannel : EventSubChannelReconciliationOutcome;

    internal sealed record MissingBot : EventSubChannelReconciliationOutcome;

    internal sealed record StartupMessageRejected : EventSubChannelReconciliationOutcome;

    internal sealed record UnresolvedDeletion : EventSubChannelReconciliationOutcome
    {
        internal required EventSubChannelFailureDetails Failure { get; init; }

        public override string ToString()
        {
            return nameof(UnresolvedDeletion);
        }
    }
}

internal abstract record EventSubStartupDeliveryOutcome
{
    private EventSubStartupDeliveryOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<Completed, TResult> completed,
        Func<Rejected, TResult> rejected
    );

    internal sealed record Completed : EventSubStartupDeliveryOutcome
    {
        internal override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<Rejected, TResult> rejected
        )
        {
            return completed(this);
        }
    }

    internal sealed record Rejected : EventSubStartupDeliveryOutcome
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

internal sealed class EventSubChannelOperations(
    BotSettings settings,
    IBotAccountProvider accounts,
    ChatIdentityResolver identities,
    EventSubClient eventSub,
    IPublicChatMessageSender sender,
    IBotChannelLifecycleNotifier lifecycle
) : IEventSubChannelOperations
{
    public ValueTask<BotAccount> ResolveAccountAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        return accounts.GetBotAccountAsync(channel, cancellationToken);
    }

    public async ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        BotAccount account,
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
                ValueTask.FromResult<EventSubSubscriptionSetupOutcome>(
                    new EventSubSubscriptionSetupOutcome.MissingChannel()
                ),
            static _ =>
                ValueTask.FromResult<EventSubSubscriptionSetupOutcome>(
                    new EventSubSubscriptionSetupOutcome.MissingBot()
                )
        );
    }

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateResolvedSubscriptionAsync(
        string channel,
        BotAccount account,
        string sessionId,
        ChatIdentityResolution.Resolved resolved,
        CancellationToken cancellationToken
    )
    {
        return new EventSubSubscriptionSetupOutcome.Created(
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
                Readiness = EventSubSubscriptionReadiness.PendingStartupDelivery,
            }
        );
    }

    public async ValueTask<EventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(settings.StartupMessage))
        {
            return new EventSubStartupDeliveryOutcome.Completed();
        }

        var outcome = await sender.SendAsync(
            channel,
            settings.StartupMessage,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        return outcome.Match<EventSubStartupDeliveryOutcome>(
            static _ => new EventSubStartupDeliveryOutcome.Completed(),
            static _ => new EventSubStartupDeliveryOutcome.Rejected()
        );
    }

    public ValueTask NotifyChannelStartedAsync(string channel, CancellationToken cancellationToken)
    {
        return new(lifecycle.ChannelStartedAsync(channel, cancellationToken));
    }

    public async ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
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
            return new EventSubSubscriptionDeletionOutcome.Deleted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new EventSubSubscriptionDeletionOutcome.Unresolved
            {
                Failure = EventSubChannelFailureClassifier.Classify(
                    exception,
                    EventSubChannelPhase.SubscriptionDeletion,
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

internal sealed class EventSubChannelSessionFactory(
    IEventSubChannelOperations operations,
    EventSubChannelRecoveryPipeline recovery,
    EventSubSubscriptionReconciliationStore pendingDeletions,
    EventSubChannelStatusStore channelStatus,
    BotRuntimeStatusStore runtimeStatus,
    IEventSubChannelDiagnosticReporter diagnostics,
    TimeProvider timeProvider
)
{
    internal bool HasPendingReconciliation => pendingDeletions.HasPendingReconciliation;

    internal EventSubChannelSession Create(string sessionId)
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

internal sealed class EventSubChannelSession(
    string sessionId,
    IEventSubChannelOperations operations,
    EventSubChannelRecoveryPipeline recovery,
    EventSubSubscriptionReconciliationStore pendingDeletions,
    EventSubChannelStatusStore.EventSubChannelStatusScope statusScope,
    BotRuntimeStatusStore runtimeStatus,
    IEventSubChannelDiagnosticReporter diagnostics,
    TimeProvider timeProvider
) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ActiveEventSubSubscription> _subscriptions = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, EventSubChannelStatus> _states = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, EventSubChannelFailureContext> _failures = new(
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
        var desired = BotChannelList.Normalize(desiredChannels);
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
                                ? EventSubChannelReconciliationTarget.Present
                                : EventSubChannelReconciliationTarget.Absent,
                            EventSubChannelRecoveryTrigger.Startup,
                            token
                        )
                    )
                )
            );
        }
    }

    internal void TriggerReconciliation(
        IReadOnlyList<string> desiredChannels,
        EventSubChannelRecoveryTrigger trigger
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
            var desired = BotChannelList.Normalize(desiredChannels);
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
        EventSubChannelRecoveryTrigger trigger,
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

        var desired = BotChannelList.Normalize(desiredChannels);
        var removed = trackedChannels.Except(desired, StringComparer.OrdinalIgnoreCase).ToArray();
        await Task.WhenAll(
            desired
                .Select(channel =>
                    RunTriggeredAsync(
                        channel,
                        EventSubChannelReconciliationTarget.Present,
                        trigger,
                        cancellationToken
                    )
                )
                .Concat(
                    removed.Select(channel =>
                        RunTriggeredAsync(
                            channel,
                            EventSubChannelReconciliationTarget.Absent,
                            trigger,
                            cancellationToken
                        )
                    )
                )
        );
    }

    private async Task RunTriggeredAsync(
        string channel,
        EventSubChannelReconciliationTarget target,
        EventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        EventSubChannelStatus? state;
        lock (_gate)
        {
            _states.TryGetValue(channel, out state);
        }

        if (state is EventSubChannelStatus.Degraded degraded)
        {
            if (
                target is EventSubChannelReconciliationTarget.Present
                && degraded.NextAction is EventSubChannelNextAction.NoFurtherAction
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
        EventSubChannelReconciliationTarget target,
        EventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        var context = new EventSubChannelAttemptContext();
        EventSubChannelReconciliationOutcome outcome;
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
            var failureDetails = EventSubChannelFailureClassifier.Classify(
                exception,
                context.Phase,
                cancellationToken
            );
            var failure = new EventSubChannelFailureContext.ClassifiedException(failureDetails);
            RetainPendingDeletionFailure(channel, failureDetails);
            var isRecoverable = EventSubChannelFailureClassifier.IsRecoverable(
                failure.Classification
            );
            PublishDegraded(
                channel,
                trigger,
                attempt: 1,
                failure,
                isRecoverable
                    ? EventSubChannelNextAction.BeginRecoveryCycle
                    : EventSubChannelNextAction.RetryOnNextReconciliation
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

        ValueTask PublishOutcomeAsync(EventSubChannelReconciliationOutcome result)
        {
            PublishReconciliationOutcome(channel, target, trigger, attempt: 1, result);
            return ValueTask.CompletedTask;
        }

        async ValueTask HandleUnresolvedDeletionAsync(
            EventSubChannelReconciliationOutcome.UnresolvedDeletion unresolved
        )
        {
            var failure = new EventSubChannelFailureContext.ClassifiedException(unresolved.Failure);
            var isRecoverable = EventSubChannelFailureClassifier.IsRecoverable(
                failure.Classification
            );
            PublishDegraded(
                channel,
                trigger,
                attempt: 1,
                failure,
                isRecoverable
                    ? EventSubChannelNextAction.BeginRecoveryCycle
                    : EventSubChannelNextAction.RetryOnNextReconciliation
            );
            if (isRecoverable)
            {
                await RunRecoveryCycleAsync(channel, target, trigger, failure, cancellationToken);
            }
        }
    }

    private EventSubChannelFailureContext GetFailureContext(string channel)
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

    private void RetainPendingDeletionFailure(string channel, EventSubChannelFailureDetails failure)
    {
        if (
            failure.Phase is not EventSubChannelPhase.SubscriptionDeletion
            || failure.Classification is EventSubChannelFailureClassification.Cancellation
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
        EventSubChannelReconciliationTarget target,
        EventSubChannelRecoveryTrigger trigger,
        EventSubChannelFailureContext initialFailure,
        CancellationToken cancellationToken
    )
    {
        var attempt = 0;
        var latestFailure = initialFailure;
        var context = new EventSubChannelAttemptContext { Phase = initialFailure.Phase };
        EventSubChannelReconciliationOutcome outcome;
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
                            is EventSubChannelReconciliationOutcome.UnresolvedDeletion unresolved
                        )
                        {
                            latestFailure = new EventSubChannelFailureContext.ClassifiedException(
                                unresolved.Failure
                            );
                        }

                        return outcome;
                    }
                    catch (Exception exception)
                    {
                        var failureDetails = EventSubChannelFailureClassifier.Classify(
                            exception,
                            context.Phase,
                            cancellationToken
                        );
                        latestFailure = new EventSubChannelFailureContext.ClassifiedException(
                            failureDetails
                        );
                        RetainPendingDeletionFailure(channel, failureDetails);
                        throw;
                    }
                },
                cancellationToken
            );
        }
        catch (EventSubChannelStatusPublicationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            var failureDetails = EventSubChannelFailureClassifier.Classify(
                exception,
                context.Phase,
                cancellationToken
            );
            latestFailure = new EventSubChannelFailureContext.ClassifiedException(failureDetails);
            RetainPendingDeletionFailure(channel, failureDetails);
            PublishDegraded(
                channel,
                trigger,
                attempt,
                latestFailure,
                EventSubChannelNextAction.RetryOnNextReconciliation
            );
            return;
        }

        PublishReconciliationOutcome(channel, target, trigger, attempt, outcome);
    }

    private ValueTask<EventSubChannelReconciliationOutcome> ReconcileAsync(
        string channel,
        EventSubChannelReconciliationTarget target,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        return target switch
        {
            EventSubChannelReconciliationTarget.Present => EnsurePresentAsync(
                channel,
                context,
                cancellationToken
            ),
            EventSubChannelReconciliationTarget.Absent => EnsureAbsentAsync(
                channel,
                context,
                cancellationToken
            ),
            _ => throw new UnreachableException("Unknown EventSub channel reconciliation target."),
        };
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> EnsurePresentAsync(
        string channel,
        EventSubChannelAttemptContext context,
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
            case EventSubChannelReconciliationOutcome.Completed:
                break;
            case EventSubChannelReconciliationOutcome.UnresolvedDeletion:
                return pendingDeletion;
            default:
                throw new UnreachableException(
                    "Pending EventSub deletion produced a non-deletion reconciliation outcome."
                );
        }

        await CompletePendingStopAsync(channel, context, cancellationToken);
        var account = await RunPhaseAsync(
            context,
            EventSubChannelPhase.AccountResolution,
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
                case EventSubChannelReconciliationOutcome.Completed:
                    break;
                case EventSubChannelReconciliationOutcome.UnresolvedDeletion:
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
                EventSubChannelPhase.SubscriptionSetup,
                token => operations.CreateSubscriptionAsync(channel, account, sessionId, token),
                cancellationToken
            );
            switch (setup)
            {
                case EventSubSubscriptionSetupOutcome.Created created:
                    active = created.Subscription;
                    break;
                case EventSubSubscriptionSetupOutcome.MissingChannel:
                    return new EventSubChannelReconciliationOutcome.MissingChannel();
                case EventSubSubscriptionSetupOutcome.MissingBot:
                    return new EventSubChannelReconciliationOutcome.MissingBot();
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
            case EventSubSubscriptionReadiness.PendingStartupDelivery:
                var startupDelivery = await RunPhaseAsync(
                    context,
                    EventSubChannelPhase.SubscriptionSetup,
                    token => operations.DeliverStartupMessageAsync(channel, token),
                    cancellationToken
                );
                if (!startupDelivery.Match(static _ => true, static _ => false))
                {
                    return new EventSubChannelReconciliationOutcome.StartupMessageRejected();
                }

                active = active with
                {
                    Readiness = EventSubSubscriptionReadiness.PendingLifecycleStart,
                };
                lock (_gate)
                {
                    _subscriptions[channel] = active;
                }

                goto case EventSubSubscriptionReadiness.PendingLifecycleStart;
            case EventSubSubscriptionReadiness.PendingLifecycleStart:
                await RunPhaseAsync(
                    context,
                    EventSubChannelPhase.SubscriptionSetup,
                    token => operations.NotifyChannelStartedAsync(channel, token),
                    cancellationToken
                );
                active = active with { Readiness = EventSubSubscriptionReadiness.Ready };
                lock (_gate)
                {
                    _subscriptions[channel] = active;
                }

                break;
            case EventSubSubscriptionReadiness.Ready:
                break;
            default:
                throw new UnreachableException("Unknown EventSub subscription setup stage.");
        }

        context.Phase = EventSubChannelPhase.Reconciliation;
        return new EventSubChannelReconciliationOutcome.Completed();
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> EnsureAbsentAsync(
        string channel,
        EventSubChannelAttemptContext context,
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
            case EventSubChannelReconciliationOutcome.Completed:
                break;
            case EventSubChannelReconciliationOutcome.UnresolvedDeletion:
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
                case EventSubChannelReconciliationOutcome.Completed:
                    break;
                case EventSubChannelReconciliationOutcome.UnresolvedDeletion:
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

        context.Phase = EventSubChannelPhase.Reconciliation;
        return new EventSubChannelReconciliationOutcome.Completed();
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> ReconcilePendingDeletionAsync(
        string channel,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        if (!pendingDeletions.TryGet(channel, out var pending))
        {
            return new EventSubChannelReconciliationOutcome.Completed();
        }

        return await ReconcileSubscriptionDeletionAsync(
            pending.Subscription,
            context,
            cancellationToken
        );
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> ReconcileSubscriptionDeletionAsync(
        ActiveEventSubSubscription subscription,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        pendingDeletions.Begin(subscription);
        var outcome = await RunPhaseAsync(
            context,
            EventSubChannelPhase.SubscriptionDeletion,
            token => operations.DeleteSubscriptionAsync(subscription, token),
            cancellationToken
        );
        switch (outcome)
        {
            case EventSubSubscriptionDeletionOutcome.Deleted:
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
                return new EventSubChannelReconciliationOutcome.Completed();
            case EventSubSubscriptionDeletionOutcome.Unresolved unresolved:
                pendingDeletions.RetainUnresolved(subscription, unresolved.Failure);
                return new EventSubChannelReconciliationOutcome.UnresolvedDeletion
                {
                    Failure = unresolved.Failure,
                };
            default:
                throw new UnreachableException("Unknown EventSub subscription-deletion outcome.");
        }
    }

    private async ValueTask CompletePendingStopAsync(
        string channel,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        if (!pendingDeletions.HasPendingStop(channel))
        {
            return;
        }

        await RunPhaseAsync(
            context,
            EventSubChannelPhase.Reconciliation,
            token => operations.CompleteStopAsync(channel, token),
            cancellationToken
        );
        pendingDeletions.ConfirmStopped(channel);
    }

    private static async ValueTask<T> RunPhaseAsync<T>(
        EventSubChannelAttemptContext context,
        EventSubChannelPhase phase,
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
            throw new EventSubChannelOperationException(phase, exception);
        }
    }

    private static async ValueTask RunPhaseAsync(
        EventSubChannelAttemptContext context,
        EventSubChannelPhase phase,
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
            throw new EventSubChannelOperationException(phase, exception);
        }
    }

    private void PublishReconciliationOutcome(
        string channel,
        EventSubChannelReconciliationTarget target,
        EventSubChannelRecoveryTrigger trigger,
        int attempt,
        EventSubChannelReconciliationOutcome outcome
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
                            new EventSubChannelFailureContext.MissingChannel(),
                            EventSubChannelNextAction.RetryOnNextReconciliation
                        ),
                _ =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new EventSubChannelFailureContext.MissingBot(),
                            EventSubChannelNextAction.RetryOnNextReconciliation
                        ),
                _ =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new EventSubChannelFailureContext.StartupMessageRejected(),
                            EventSubChannelNextAction.NoFurtherAction
                        ),
                unresolved =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new EventSubChannelFailureContext.ClassifiedException(
                                unresolved.Failure
                            ),
                            EventSubChannelNextAction.RetryOnNextReconciliation
                        )
            )
            .Invoke();
    }

    private void PublishSuccess(
        string channel,
        EventSubChannelReconciliationTarget target,
        int attempt,
        EventSubChannelRecoveryTrigger trigger
    )
    {
        switch (target)
        {
            case EventSubChannelReconciliationTarget.Present:
                Publish(
                    new EventSubChannelDiagnosticReport.Healthy
                    {
                        ChannelStatus = new EventSubChannelStatus.Healthy
                        {
                            Channel = channel,
                            Phase = EventSubChannelPhase.Reconciliation,
                            Attempt = attempt,
                            ChangedAt = timeProvider.GetUtcNow(),
                            Trigger = trigger,
                        },
                    }
                );
                return;
            case EventSubChannelReconciliationTarget.Absent:
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
        EventSubChannelRecoveryTrigger trigger,
        int attempt,
        EventSubChannelFailureContext failure
    )
    {
        Publish(
            new EventSubChannelDiagnosticReport.Recovering
            {
                ChannelStatus = new EventSubChannelStatus.Recovering
                {
                    Channel = channel,
                    Phase = failure.Phase,
                    Attempt = attempt,
                    ChangedAt = timeProvider.GetUtcNow(),
                    Trigger = trigger,
                    Failure = failure.ToPublicFailure(),
                    NextAction = EventSubChannelNextAction.ContinueRecoveryCycle,
                },
                Failure = failure,
            }
        );
    }

    private void PublishDegraded(
        string channel,
        EventSubChannelRecoveryTrigger trigger,
        int attempt,
        EventSubChannelFailureContext failure,
        EventSubChannelNextAction nextAction
    )
    {
        if (failure.Phase is EventSubChannelPhase.AccountResolution)
        {
            lock (_gate)
            {
                _authorizedChannels.Remove(channel);
            }
        }

        Publish(
            new EventSubChannelDiagnosticReport.Degraded
            {
                ChannelStatus = new EventSubChannelStatus.Degraded
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

    private void Publish(EventSubChannelDiagnosticReport report)
    {
        try
        {
            var state = report.Status;
            lock (_gate)
            {
                _states[state.Channel] = state;
                switch (report)
                {
                    case EventSubChannelDiagnosticReport.Healthy:
                        _failures.Remove(state.Channel);
                        break;
                    case EventSubChannelDiagnosticReport.Recovering recovering:
                        _failures[state.Channel] = recovering.Failure;
                        break;
                    case EventSubChannelDiagnosticReport.Degraded degraded:
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
            throw new EventSubChannelStatusPublicationException(exception);
        }
    }

    private void UpdateRuntimeStatusLocked()
    {
        var healthyChannels = _states
            .Values.OfType<EventSubChannelStatus.Healthy>()
            .Select(state => state.Channel)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        BotRuntimeStatus status =
            healthyChannels.Length > 0 ? new BotRuntimeStatus.Connected(healthyChannels)
            : _authorizedChannels.Count > 0 ? new BotRuntimeStatus.Authorized()
            : new BotRuntimeStatus.Unauthorized();
        runtimeStatus.SetEventSubStatus(statusScope.Id, status);
    }

    private sealed class EventSubChannelAttemptContext
    {
        internal EventSubChannelPhase Phase { get; set; } = EventSubChannelPhase.AccountResolution;
    }
}

internal enum EventSubChannelReconciliationTarget
{
    Present,
    Absent,
}

internal sealed class EventSubChannelStatusPublicationException(Exception innerException)
    : Exception("EventSub channel status publication failed.", innerException);

internal enum EventSubSubscriptionReadiness
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

    internal required EventSubSubscriptionReadiness Readiness { get; init; }
}
