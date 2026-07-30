using System.Threading.Channels;
using BlokeBot.Commands;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Timeout;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public abstract partial class EventSubChannelRecoveryTestBase
{
    private protected sealed class ScriptedChannelOperations : IEventSubChannelOperations
    {
        private readonly Dictionary<
            string,
            Queue<
                Func<CancellationToken, ValueTask<Result<BotAccount, AccessTokenUnavailableReason>>>
            >
        > _accountScripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            string,
            Queue<Func<CancellationToken, ValueTask<BotAccount>>>
        > _broadcasterAccountScripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _accountCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, List<EventSubAuthorizationContext>> _authorizations =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            string,
            List<EventSubOperationSubscriptionKind?>
        > _operationKinds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            string,
            Queue<Func<CancellationToken, ValueTask<EventSubSubscriptionSetupOutcome>>>
        > _createScripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _createCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Exception?>> _deleteOutcomes = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Action>> _beforeDelete = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> _deleteCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, List<ActiveEventSubSubscription>> _deleteAttempts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> _startupDeliveryCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<
            string,
            Queue<EventSubStartupDeliveryOutcome>
        > _startupDeliveryOutcomes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _channelStartedCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, Queue<Exception>> _channelStartedFailures = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> _completeStopCounts = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<
            string,
            HashSet<EventSubOperationSubscriptionKind>
        > _nativeTwitchFeatures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Queue<Exception>> _completeStopFailures = new(
            StringComparer.OrdinalIgnoreCase
        );

        internal void EnqueueAccount(
            string channel,
            Func<CancellationToken, ValueTask<BotAccount>> operation
        )
        {
            GetQueue(_accountScripts, channel)
                .Enqueue(async cancellationToken =>
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(
                        await operation(cancellationToken)
                    )
                );
        }

        internal void EnqueueAccountFailure(string channel, Exception exception)
        {
            EnqueueAccount(channel, _ => ValueTask.FromException<BotAccount>(exception));
        }

        internal void EnqueueAccountResult(string channel, string botLogin)
        {
            EnqueueAccount(channel, _ => ValueTask.FromResult(new BotAccount(botLogin, "secret")));
        }

        internal void EnqueueBroadcasterAccountResult(string channel, string login)
        {
            GetQueue(_broadcasterAccountScripts, channel)
                .Enqueue(_ => ValueTask.FromResult(new BotAccount(login, "broadcaster-secret")));
        }

        internal void EnqueueAccountUnavailable(string channel, AccessTokenUnavailableReason reason)
        {
            GetQueue(_accountScripts, channel)
                .Enqueue(_ =>
                    ValueTask.FromResult(
                        Result<BotAccount, AccessTokenUnavailableReason>.Error(reason)
                    )
                );
        }

        internal int AccountCount(string channel)
        {
            return _accountCounts.GetValueOrDefault(channel);
        }

        internal IReadOnlyList<EventSubAuthorizationContext> Authorizations(string channel)
        {
            return _authorizations.TryGetValue(channel, out var values) ? values : [];
        }

        internal IReadOnlyList<EventSubOperationSubscriptionKind?> OperationKinds(string channel)
        {
            return _operationKinds.TryGetValue(channel, out var values) ? values : [];
        }

        internal void EnqueueCreateFailure(string channel, Exception exception)
        {
            EnqueueCreate(
                channel,
                _ => ValueTask.FromException<EventSubSubscriptionSetupOutcome>(exception)
            );
        }

        internal void EnqueueCreateOutcome(string channel, EventSubSubscriptionSetupOutcome outcome)
        {
            EnqueueCreate(channel, _ => ValueTask.FromResult(outcome));
        }

        internal int CreateCount(string channel)
        {
            return _createCounts.GetValueOrDefault(channel);
        }

        internal void EnqueueDeleteFailure(string channel, Exception exception)
        {
            GetQueue(_deleteOutcomes, channel).Enqueue(exception);
        }

        internal void EnqueueDeleteSuccess(string channel)
        {
            GetQueue(_deleteOutcomes, channel).Enqueue(null);
        }

        internal void EnqueueBeforeDelete(string channel, Action action)
        {
            GetQueue(_beforeDelete, channel).Enqueue(action);
        }

        internal int DeleteCount(string channel)
        {
            return _deleteCounts.GetValueOrDefault(channel);
        }

        internal IReadOnlyList<ActiveEventSubSubscription> DeleteAttempts(string channel)
        {
            return _deleteAttempts.TryGetValue(channel, out var attempts) ? attempts.ToArray() : [];
        }

        internal int StartupDeliveryCount(string channel)
        {
            return _startupDeliveryCounts.GetValueOrDefault(channel);
        }

        internal void EnqueueStartupDeliveryOutcome(
            string channel,
            EventSubStartupDeliveryOutcome outcome
        )
        {
            GetQueue(_startupDeliveryOutcomes, channel).Enqueue(outcome);
        }

        internal int ChannelStartedCount(string channel)
        {
            return _channelStartedCounts.GetValueOrDefault(channel);
        }

        internal void EnqueueChannelStartedFailure(string channel, Exception exception)
        {
            GetQueue(_channelStartedFailures, channel).Enqueue(exception);
        }

        internal int CompleteStopCount(string channel)
        {
            return _completeStopCounts.GetValueOrDefault(channel);
        }

        internal void EnqueueCompleteStopFailure(string channel, Exception exception)
        {
            GetQueue(_completeStopFailures, channel).Enqueue(exception);
        }

        internal void SetNativeTwitchEnabled(string channel, bool enabled)
        {
            _nativeTwitchFeatures[channel] = enabled
                ? Enum.GetValues<EventSubOperationSubscriptionKind>().ToHashSet()
                : [];
        }

        internal void SetNativeTwitchFeatureEnabled(
            string channel,
            EventSubOperationSubscriptionKind kind,
            bool enabled
        )
        {
            if (!_nativeTwitchFeatures.TryGetValue(channel, out var features))
            {
                features = [];
                _nativeTwitchFeatures[channel] = features;
            }

            if (enabled)
            {
                features.Add(kind);
            }
            else
            {
                features.Remove(kind);
            }
        }

        public IO<BotAccount, AccessTokenUnavailableReason> ResolveAccount(
            string channel,
            EventSubAuthorizationContext authorization
        )
        {
            return IO<BotAccount, AccessTokenUnavailableReason>.Create(cancellationToken =>
            {
                _accountCounts[channel] = AccountCount(channel) + 1;
                if (!_authorizations.TryGetValue(channel, out var authorizations))
                {
                    authorizations = [];
                    _authorizations[channel] = authorizations;
                }
                authorizations.Add(authorization);
                if (authorization is EventSubAuthorizationContext.Broadcaster)
                {
                    return
                        _broadcasterAccountScripts.TryGetValue(channel, out var broadcasters)
                        && broadcasters.Count > 0
                        ? GetBroadcasterAccountAsync(broadcasters.Dequeue(), cancellationToken)
                        : ValueTask.FromResult(
                            Result<BotAccount, AccessTokenUnavailableReason>.Error(
                                AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                            )
                        );
                }
                return _accountScripts.TryGetValue(channel, out var scripts) && scripts.Count > 0
                    ? scripts.Dequeue()(cancellationToken)
                    : ValueTask.FromResult(
                        Result<BotAccount, AccessTokenUnavailableReason>.Success(
                            new BotAccount($"{channel}-bot", $"{channel}-secret")
                        )
                    );
            });
        }

        public ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
            string channel,
            EventSubAuthorizationContext authorization,
            BotAccount account,
            string sessionId,
            CancellationToken cancellationToken,
            EventSubOperationSubscriptionKind? operationKind = null
        )
        {
            _createCounts[channel] = CreateCount(channel) + 1;
            if (!_operationKinds.TryGetValue(channel, out var operationKinds))
            {
                operationKinds = [];
                _operationKinds[channel] = operationKinds;
            }
            operationKinds.Add(operationKind);
            if (_createScripts.TryGetValue(channel, out var scripts) && scripts.Count > 0)
            {
                return scripts.Dequeue()(cancellationToken);
            }

            return ValueTask.FromResult<EventSubSubscriptionSetupOutcome>(
                new EventSubSubscriptionSetupOutcome.Created(
                    new ActiveEventSubSubscription
                    {
                        Channel = channel,
                        SubscriptionId = $"{sessionId}-{channel}",
                        BotLogin = account.Login,
                        Authorization = authorization,
                        AccessToken = account.AccessToken,
                        Readiness = EventSubSubscriptionReadiness.PendingStartupDelivery,
                    }
                )
            );
        }

        public ValueTask<EventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            _startupDeliveryCounts[channel] = StartupDeliveryCount(channel) + 1;
            EventSubStartupDeliveryOutcome outcome =
                _startupDeliveryOutcomes.TryGetValue(channel, out var outcomes)
                && outcomes.Count > 0
                    ? outcomes.Dequeue()
                    : new EventSubStartupDeliveryOutcome.Completed();
            return ValueTask.FromResult(outcome);
        }

        public ValueTask<bool> NativeTwitchFeatureIsEnabledAsync(
            string channel,
            EventSubOperationSubscriptionKind kind,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult(
                _nativeTwitchFeatures.TryGetValue(channel, out var features)
                    && features.Contains(kind)
            );
        }

        public ValueTask NotifyChannelStartedAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            _channelStartedCounts[channel] = ChannelStartedCount(channel) + 1;
            if (
                _channelStartedFailures.TryGetValue(channel, out var failures)
                && failures.Count > 0
            )
            {
                return ValueTask.FromException(failures.Dequeue());
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
            ActiveEventSubSubscription subscription,
            CancellationToken cancellationToken
        )
        {
            _deleteCounts[subscription.Channel] = DeleteCount(subscription.Channel) + 1;
            if (!_deleteAttempts.TryGetValue(subscription.Channel, out var attempts))
            {
                attempts = [];
                _deleteAttempts[subscription.Channel] = attempts;
            }

            attempts.Add(subscription);
            if (
                _beforeDelete.TryGetValue(subscription.Channel, out var actions)
                && actions.Count > 0
            )
            {
                actions.Dequeue()();
            }

            if (
                !_deleteOutcomes.TryGetValue(subscription.Channel, out var outcomes)
                || outcomes.Count == 0
                || outcomes.Dequeue() is not { } exception
            )
            {
                return ValueTask.FromResult<EventSubSubscriptionDeletionOutcome>(
                    new EventSubSubscriptionDeletionOutcome.Deleted()
                );
            }

            if (
                exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested
            )
            {
                return ValueTask.FromException<EventSubSubscriptionDeletionOutcome>(exception);
            }

            return ValueTask.FromResult<EventSubSubscriptionDeletionOutcome>(
                new EventSubSubscriptionDeletionOutcome.Unresolved
                {
                    Failure = EventSubChannelFailureClassifier.Classify(
                        exception,
                        EventSubChannelPhase.SubscriptionDeletion,
                        cancellationToken
                    ),
                }
            );
        }

        public ValueTask CompleteStopAsync(string channel, CancellationToken cancellationToken)
        {
            _completeStopCounts[channel] = CompleteStopCount(channel) + 1;
            return
                _completeStopFailures.TryGetValue(channel, out var failures) && failures.Count > 0
                ? ValueTask.FromException(failures.Dequeue())
                : ValueTask.CompletedTask;
        }

        private static async ValueTask<
            Result<BotAccount, AccessTokenUnavailableReason>
        > GetBroadcasterAccountAsync(
            Func<CancellationToken, ValueTask<BotAccount>> operation,
            CancellationToken cancellationToken
        )
        {
            return Result<BotAccount, AccessTokenUnavailableReason>.Success(
                await operation(cancellationToken)
            );
        }

        private static Queue<TValue> GetQueue<TValue>(
            Dictionary<string, Queue<TValue>> queues,
            string channel
        )
        {
            if (!queues.TryGetValue(channel, out var queue))
            {
                queue = new Queue<TValue>();
                queues[channel] = queue;
            }

            return queue;
        }

        private void EnqueueCreate(
            string channel,
            Func<CancellationToken, ValueTask<EventSubSubscriptionSetupOutcome>> operation
        )
        {
            GetQueue(_createScripts, channel).Enqueue(operation);
        }
    }
}
