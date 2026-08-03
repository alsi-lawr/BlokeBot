using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubChannelStatusStore : IEventSubChannelStatusAccessor
{
    private readonly object _gate = new();
    private long _nextScopeId;
    private long _activeScopeId;

    public event Action? Changed;

    public EventSubChannelStatusSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return field;
            }
        }
        private set;
    } = new() { Channels = Array.Empty<EventSubChannelStatus>() };

    internal EventSubChannelStatusScope CreateScope()
    {
        lock (_gate)
        {
            checked
            {
                _nextScopeId++;
            }

            return new EventSubChannelStatusScope(this, _nextScopeId);
        }
    }

    private void Activate(EventSubChannelStatusScope scope)
    {
        Action? changed;
        lock (_gate)
        {
            _activeScopeId = scope.Id;
            Current = CreateSnapshot(scope.States);
            changed = Changed;
        }

        changed?.Invoke();
    }

    private void Set(EventSubChannelStatusScope scope, EventSubChannelStatus status)
    {
        Action? changed = null;
        lock (_gate)
        {
            scope.States[status.Channel] = status;
            if (_activeScopeId == scope.Id)
            {
                Current = CreateSnapshot(scope.States);
                changed = Changed;
            }
        }

        changed?.Invoke();
    }

    private void Remove(EventSubChannelStatusScope scope, string channel)
    {
        Action? changed = null;
        lock (_gate)
        {
            if (!scope.States.Remove(channel) || _activeScopeId != scope.Id)
            {
                return;
            }

            Current = CreateSnapshot(scope.States);
            changed = Changed;
        }

        changed?.Invoke();
    }

    private void Deactivate(EventSubChannelStatusScope scope)
    {
        Action? changed = null;
        lock (_gate)
        {
            if (_activeScopeId != scope.Id)
            {
                return;
            }

            _activeScopeId = 0;
            Current = new EventSubChannelStatusSnapshot
            {
                Channels = Array.Empty<EventSubChannelStatus>(),
            };
            changed = Changed;
        }

        changed?.Invoke();
    }

    private static EventSubChannelStatusSnapshot CreateSnapshot(
        Dictionary<string, EventSubChannelStatus> states
    ) =>
        new()
        {
            Channels = Array.AsReadOnly(
                states
                    .Values.OrderBy(static state => state.Channel, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            ),
        };

    internal sealed class EventSubChannelStatusScope(EventSubChannelStatusStore owner, long id)
        : IDisposable
    {
        internal long Id { get; } = id;

        internal Dictionary<string, EventSubChannelStatus> States { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        internal void Activate() => owner.Activate(this);

        internal void Set(EventSubChannelStatus status) => owner.Set(this, status);

        internal void Remove(string channel) => owner.Remove(this, channel);

        public void Dispose() => owner.Deactivate(this);
    }
}

internal abstract record EventSubChannelDiagnosticReport
{
    private EventSubChannelDiagnosticReport() { }

    internal abstract EventSubChannelStatus Status { get; }

    internal sealed record Healthy : EventSubChannelDiagnosticReport
    {
        internal required EventSubChannelStatus.Healthy ChannelStatus { get; init; }

        internal override EventSubChannelStatus Status => ChannelStatus;
    }

    internal sealed record Recovering : EventSubChannelDiagnosticReport
    {
        internal required EventSubChannelStatus.Recovering ChannelStatus { get; init; }

        internal required EventSubChannelFailureContext Failure { get; init; }

        internal override EventSubChannelStatus Status => ChannelStatus;
    }

    internal sealed record Degraded : EventSubChannelDiagnosticReport
    {
        internal required EventSubChannelStatus.Degraded ChannelStatus { get; init; }

        internal required EventSubChannelFailureContext Failure { get; init; }

        internal override EventSubChannelStatus Status => ChannelStatus;
    }
}

internal interface IEventSubChannelDiagnosticReporter
{
    void Report(EventSubChannelDiagnosticReport report);
}

internal sealed class EventSubChannelDiagnosticLogger(ILogger<EventSubChannelDiagnosticLogger> log)
    : IEventSubChannelDiagnosticReporter
{
    public void Report(EventSubChannelDiagnosticReport report)
    {
        switch (report)
        {
            case EventSubChannelDiagnosticReport.Healthy { ChannelStatus: var healthy }:
                log.LogInformation(
                    "EventSub channel {Channel} is healthy after {Phase} attempt {Attempt} at {ChangedAt} from {Trigger}.",
                    healthy.Channel,
                    healthy.Phase,
                    healthy.Attempt,
                    healthy.ChangedAt,
                    healthy.Trigger
                );
                return;
            case EventSubChannelDiagnosticReport.Recovering
            {
                ChannelStatus: var recovering,
                Failure: var recoveringContext,
            }:
                var recoveringFailure = CreationFailure(recoveringContext);
                log.LogWarning(
                    "EventSub channel {Channel} is recovering at {Phase} attempt {Attempt} at {ChangedAt} from {Trigger}; classified {Classification} ({FailureType}), next {NextAction}; Twitch status {TwitchStatus}, error {TwitchError}, message {TwitchMessage}, existing subscription {ExistingSubscriptionId}.",
                    recovering.Channel,
                    recovering.Phase,
                    recovering.Attempt,
                    recovering.ChangedAt,
                    recovering.Trigger,
                    recovering.Failure.Classification,
                    recovering.Failure.FailureType,
                    recovering.NextAction,
                    recoveringFailure?.StatusCode,
                    recoveringFailure?.ProviderError,
                    recoveringFailure?.ProviderMessage,
                    recoveringFailure?.ExistingSubscriptionId
                );
                return;
            case EventSubChannelDiagnosticReport.Degraded
            {
                ChannelStatus: var degraded,
                Failure: var degradedContext,
            }:
                var degradedFailure = CreationFailure(degradedContext);
                log.LogError(
                    "EventSub channel {Channel} is degraded at {Phase} attempt {Attempt} at {ChangedAt} from {Trigger}; classified {Classification} ({FailureType}), next {NextAction}; Twitch status {TwitchStatus}, error {TwitchError}, message {TwitchMessage}, existing subscription {ExistingSubscriptionId}.",
                    degraded.Channel,
                    degraded.Phase,
                    degraded.Attempt,
                    degraded.ChangedAt,
                    degraded.Trigger,
                    degraded.Failure.Classification,
                    degraded.Failure.FailureType,
                    degraded.NextAction,
                    degradedFailure?.StatusCode,
                    degradedFailure?.ProviderError,
                    degradedFailure?.ProviderMessage,
                    degradedFailure?.ExistingSubscriptionId
                );
                return;
            default:
                throw new UnreachableException("Unknown EventSub channel diagnostic report.");
        }
    }

    private static EventSubSubscriptionCreationException? CreationFailure(
        EventSubChannelFailureContext failure
    ) =>
        failure
            is EventSubChannelFailureContext.ClassifiedException
            {
                Details.Exception: EventSubSubscriptionCreationException exception,
            }
            ? exception
            : null;
}
