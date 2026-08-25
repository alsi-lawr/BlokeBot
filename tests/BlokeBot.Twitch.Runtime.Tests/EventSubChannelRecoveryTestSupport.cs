using System.Runtime.CompilerServices;
using Polly;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public abstract partial class EventSubChannelRecoveryTestBase
{
    private static readonly ConditionalWeakTable<EventSubChannelSession, TargetSet> _targets =
        new();

    private protected static readonly DateTimeOffset Now = new(
        2026,
        7,
        11,
        12,
        0,
        0,
        TimeSpan.Zero
    );

    private protected static RecoveryHarness CreateHarness(
        ScriptedChannelOperations operations,
        int attemptLimit,
        EventSubChannelStatusStore? sharedStatus = null,
        BotRuntimeStatusStore? sharedRuntimeStatus = null,
        EventSubSubscriptionReconciliationStore? sharedPendingDeletions = null
    )
    {
        var clock = new FixedTimeProvider(Now);
        var attemptBuilder = new ResiliencePipelineBuilder { TimeProvider = clock };
        var recoveryBuilder = new ResiliencePipelineBuilder<EventSubChannelReconciliationOutcome>
        {
            TimeProvider = clock,
        };
        var policy = new EventSubChannelRecoveryPolicy
        {
            AttemptLimit = attemptLimit,
            Delay = TimeSpan.Zero,
            MaximumDelay = TimeSpan.Zero,
            DelayBackoffType = DelayBackoffType.Constant,
            AttemptTimeout = TimeSpan.FromMinutes(1),
        };
        EventSubChannelRecoveryResilience.ConfigureAttempt(attemptBuilder, policy);
        EventSubChannelRecoveryResilience.Configure(recoveryBuilder, policy);
        var status = sharedStatus ?? new EventSubChannelStatusStore();
        var runtimeStatus = sharedRuntimeStatus ?? new BotRuntimeStatusStore();
        var pendingDeletions =
            sharedPendingDeletions ?? new EventSubSubscriptionReconciliationStore();
        var diagnostics = new RecordingDiagnostics();
        return new RecoveryHarness(
            new EventSubChannelSession(
                operations,
                new EventSubChannelRecoveryPipeline(
                    attemptBuilder.Build(),
                    recoveryBuilder.Build()
                ),
                pendingDeletions,
                status.CreateScope(),
                runtimeStatus,
                diagnostics,
                clock
            ),
            status,
            runtimeStatus,
            pendingDeletions,
            diagnostics,
            clock
        );
    }

    private protected static void Start(
        RecoveryHarness harness,
        IReadOnlyList<string> channels,
        CancellationToken cancellationToken
    ) => harness.Session.Start(TargetsFor(harness.Session, channels), cancellationToken);

    private protected static Task ReconcileAsync(
        EventSubChannelSession session,
        IReadOnlyList<string> desiredChannels,
        EventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken = default
    ) =>
        session.TriggerReconciliationAndDrainAsync(
            _ => ValueTask.FromResult(TargetsFor(session, desiredChannels)),
            trigger,
            cancellationToken
        );

    private protected static IReadOnlyList<BotChannelTarget> TargetsFor(
        EventSubChannelSession session,
        IEnumerable<string> channels
    ) => _targets.GetOrCreateValue(session).For(channels);

    private protected static BotChannelTarget ReplaceTarget(
        EventSubChannelSession session,
        string channel
    ) => _targets.GetOrCreateValue(session).Replace(channel);

    private protected static void AssertHealthy(
        EventSubChannelStatus.Healthy status,
        string channel,
        EventSubChannelRecoveryTrigger trigger,
        int attempt,
        DateTimeOffset changedAt
    )
    {
        status.Channel.ShouldBe(channel);
        status.Phase.ShouldBe(EventSubChannelPhase.Reconciliation);
        status.Attempt.ShouldBe(attempt);
        status.ChangedAt.ShouldBe(changedAt);
        status.Trigger.ShouldBe(trigger);
    }

    private protected static EventSubChannelFailureDetails ClassifiedFailure(
        EventSubChannelFailureContext context
    ) => context.ShouldBeOfType<EventSubChannelFailureContext.ClassifiedException>().Details;

    private protected static void AssertFailure(
        EventSubChannelStatus status,
        string channel,
        EventSubChannelPhase phase,
        EventSubChannelFailureClassification classification,
        Type failureType,
        int attempt,
        EventSubChannelRecoveryTrigger trigger,
        EventSubChannelNextAction nextAction,
        DateTimeOffset changedAt
    ) =>
        AssertFailure(
            status,
            channel,
            phase,
            classification,
            failureType.FullName!,
            attempt,
            trigger,
            nextAction,
            changedAt
        );

    private protected static void AssertFailure(
        EventSubChannelStatus status,
        string channel,
        EventSubChannelPhase phase,
        EventSubChannelFailureClassification classification,
        string failureType,
        int attempt,
        EventSubChannelRecoveryTrigger trigger,
        EventSubChannelNextAction nextAction,
        DateTimeOffset changedAt
    )
    {
        status.Channel.ShouldBe(channel);
        status.Phase.ShouldBe(phase);
        status.Attempt.ShouldBe(attempt);
        status.ChangedAt.ShouldBe(changedAt);
        status.Trigger.ShouldBe(trigger);
        status
            .Match(
                static _ => throw new InvalidOperationException("Expected a failed channel state."),
                static recovering => (recovering.Failure, recovering.NextAction),
                static degraded => (degraded.Failure, degraded.NextAction)
            )
            .ShouldBe(
                (
                    new EventSubChannelFailure
                    {
                        Classification = classification,
                        FailureType = failureType,
                    },
                    nextAction
                )
            );
    }

    private sealed class TargetSet
    {
        private readonly Dictionary<string, BotChannelSessionIdentity> _sessions = new(
            StringComparer.OrdinalIgnoreCase
        );

        internal IReadOnlyList<BotChannelTarget> For(IEnumerable<string> channels) =>
            channels
                .Select(channel =>
                {
                    if (!_sessions.TryGetValue(channel, out var identity))
                    {
                        identity = BotChannelSessionIdentity.Create();
                        _sessions[channel] = identity;
                    }

                    return new BotChannelTarget(channel, identity);
                })
                .ToArray();

        internal BotChannelTarget Replace(string channel)
        {
            var identity = BotChannelSessionIdentity.Create();
            _sessions[channel] = identity;
            return new BotChannelTarget(channel, identity);
        }
    }

    private protected sealed class RecoveryHarness(
        EventSubChannelSession session,
        EventSubChannelStatusStore status,
        BotRuntimeStatusStore runtimeStatus,
        EventSubSubscriptionReconciliationStore pendingDeletions,
        RecordingDiagnostics diagnostics,
        FixedTimeProvider clock
    ) : IAsyncDisposable
    {
        internal EventSubChannelSession Session { get; } = session;

        internal EventSubChannelStatusStore Status { get; } = status;

        internal BotRuntimeStatusStore RuntimeStatus { get; } = runtimeStatus;

        internal EventSubSubscriptionReconciliationStore PendingDeletions { get; } =
            pendingDeletions;

        internal RecordingDiagnostics Diagnostics { get; } = diagnostics;

        internal FixedTimeProvider Clock { get; } = clock;

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }
}
