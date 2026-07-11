using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchEventSubChannelStatusStore
    : ITwitchEventSubChannelStatusAccessor
{
    private readonly object gate = new();
    private long nextScopeId;
    private long activeScopeId;
    private TwitchEventSubChannelStatusSnapshot current = new()
    {
        Channels = Array.Empty<TwitchEventSubChannelStatus>(),
    };

    public event Action? Changed;

    public TwitchEventSubChannelStatusSnapshot Current
    {
        get
        {
            lock (gate)
                return current;
        }
    }

    internal TwitchEventSubChannelStatusScope CreateScope()
    {
        lock (gate)
        {
            checked
            {
                nextScopeId++;
            }

            return new TwitchEventSubChannelStatusScope(this, nextScopeId);
        }
    }

    private void Activate(TwitchEventSubChannelStatusScope scope)
    {
        Action? changed;
        lock (gate)
        {
            activeScopeId = scope.Id;
            current = CreateSnapshot(scope.States);
            changed = Changed;
        }

        changed?.Invoke();
    }

    private void Set(
        TwitchEventSubChannelStatusScope scope,
        TwitchEventSubChannelStatus status
    )
    {
        Action? changed = null;
        lock (gate)
        {
            scope.States[status.Channel] = status;
            if (activeScopeId == scope.Id)
            {
                current = CreateSnapshot(scope.States);
                changed = Changed;
            }
        }

        changed?.Invoke();
    }

    private void Remove(TwitchEventSubChannelStatusScope scope, string channel)
    {
        Action? changed = null;
        lock (gate)
        {
            if (!scope.States.Remove(channel) || activeScopeId != scope.Id)
                return;

            current = CreateSnapshot(scope.States);
            changed = Changed;
        }

        changed?.Invoke();
    }

    private void Deactivate(TwitchEventSubChannelStatusScope scope)
    {
        Action? changed = null;
        lock (gate)
        {
            if (activeScopeId != scope.Id)
                return;

            activeScopeId = 0;
            current = new TwitchEventSubChannelStatusSnapshot
            {
                Channels = Array.Empty<TwitchEventSubChannelStatus>(),
            };
            changed = Changed;
        }

        changed?.Invoke();
    }

    private static TwitchEventSubChannelStatusSnapshot CreateSnapshot(
        Dictionary<string, TwitchEventSubChannelStatus> states
    ) =>
        new()
        {
            Channels = Array.AsReadOnly(
                states.Values
                    .OrderBy(state => state.Channel, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            ),
        };

    internal sealed class TwitchEventSubChannelStatusScope(
        TwitchEventSubChannelStatusStore owner,
        long id
    ) : IDisposable
    {
        internal long Id { get; } = id;

        internal Dictionary<string, TwitchEventSubChannelStatus> States { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        internal void Activate() => owner.Activate(this);

        internal void Set(TwitchEventSubChannelStatus status) => owner.Set(this, status);

        internal void Remove(string channel) => owner.Remove(this, channel);

        public void Dispose() => owner.Deactivate(this);
    }
}

internal abstract record TwitchEventSubChannelDiagnosticReport
{
    private protected TwitchEventSubChannelDiagnosticReport() { }

    internal abstract TwitchEventSubChannelStatus Status { get; }

    private protected abstract void Seal();

    internal sealed record Healthy : TwitchEventSubChannelDiagnosticReport
    {
        internal required TwitchEventSubChannelStatus.Healthy ChannelStatus
        {
            get;
            init;
        }

        internal override TwitchEventSubChannelStatus Status => ChannelStatus;

        private protected override void Seal() { }
    }

    internal sealed record Recovering : TwitchEventSubChannelDiagnosticReport
    {
        internal required TwitchEventSubChannelStatus.Recovering ChannelStatus
        {
            get;
            init;
        }

        internal required TwitchEventSubChannelFailureDetails Failure { get; init; }

        internal override TwitchEventSubChannelStatus Status => ChannelStatus;

        private protected override void Seal() { }
    }

    internal sealed record Degraded : TwitchEventSubChannelDiagnosticReport
    {
        internal required TwitchEventSubChannelStatus.Degraded ChannelStatus
        {
            get;
            init;
        }

        internal required TwitchEventSubChannelFailureDetails Failure { get; init; }

        internal override TwitchEventSubChannelStatus Status => ChannelStatus;

        private protected override void Seal() { }
    }
}

internal interface ITwitchEventSubChannelDiagnosticReporter
{
    void Report(TwitchEventSubChannelDiagnosticReport report);
}

internal sealed class TwitchEventSubChannelDiagnosticLogger(
    ILogger<TwitchEventSubChannelDiagnosticLogger> log
) : ITwitchEventSubChannelDiagnosticReporter
{
    public void Report(TwitchEventSubChannelDiagnosticReport report)
    {
        switch (report)
        {
            case TwitchEventSubChannelDiagnosticReport.Healthy
            {
                ChannelStatus: var healthy,
            }:
                log.LogInformation(
                    "EventSub channel {Channel} is healthy after {Phase} attempt {Attempt} at {ChangedAt} from {Trigger}.",
                    healthy.Channel,
                    healthy.Phase,
                    healthy.Attempt,
                    healthy.ChangedAt,
                    healthy.Trigger
                );
                return;
            case TwitchEventSubChannelDiagnosticReport.Recovering
            {
                ChannelStatus: var recovering,
            }:
                log.LogWarning(
                    "EventSub channel {Channel} is recovering at {Phase} attempt {Attempt} at {ChangedAt} from {Trigger}; classified {Classification} ({FailureType}), next {NextAction}.",
                    recovering.Channel,
                    recovering.Phase,
                    recovering.Attempt,
                    recovering.ChangedAt,
                    recovering.Trigger,
                    recovering.Failure.Classification,
                    recovering.Failure.FailureType,
                    recovering.NextAction
                );
                return;
            case TwitchEventSubChannelDiagnosticReport.Degraded
            {
                ChannelStatus: var degraded,
            }:
                log.LogError(
                    "EventSub channel {Channel} is degraded at {Phase} attempt {Attempt} at {ChangedAt} from {Trigger}; classified {Classification} ({FailureType}), next {NextAction}.",
                    degraded.Channel,
                    degraded.Phase,
                    degraded.Attempt,
                    degraded.ChangedAt,
                    degraded.Trigger,
                    degraded.Failure.Classification,
                    degraded.Failure.FailureType,
                    degraded.NextAction
                );
                return;
            default:
                throw new UnreachableException(
                    "Unknown EventSub channel diagnostic report."
                );
        }
    }
}
