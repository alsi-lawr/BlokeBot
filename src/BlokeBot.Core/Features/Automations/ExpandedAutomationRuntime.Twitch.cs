using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using static BlokeBot.Core.Features.Automations.ExpandedAutomationContext;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class ExpandedAutomationRuntime
{
    public async Task GoalChangedAsync(EventSubGoalEvent goal, CancellationToken cancellation)
    {
        var host = await HostAsync(goal.BroadcasterId, HostFeatureFlags.None, cancellation);
        if (
            host is null
            || !await AcceptEventAsync(host.Id, goal.Timestamp, cancellation)
            || !await AuthorizedAsync(host, ["channel:read:goals"], cancellation)
        )
        {
            return;
        }
        var sources = (await SourcesAsync(host, cancellation))
            .OfType<GoalSourceConfiguration>()
            .ToArray();
        var epoch = GoalEpoch(host.Id, sources);
        if (sources.Length == 0)
        {
            return;
        }
        var crossed = new List<long>();
        await using (var db = await dbFactory.CreateDbContextAsync(cancellation))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
            _ = await MainDatabaseStatements.LockHostAsync(db, host.Id, cancellation);
            var row = await db.AutomationGoalObservations.SingleOrDefaultAsync(
                g => g.HostId == host.Id && g.GoalId == goal.GoalId,
                cancellation
            );
            if (row is not null && (row.IsEnded || row.ObservedAtUtc >= goal.Timestamp.UtcDateTime))
            {
                return;
            }
            var baseline = row is null || row.ConnectionId != epoch;
            if (row is null)
            {
                row = new() { HostId = host.Id, GoalId = goal.GoalId };
                _ = db.AutomationGoalObservations.Add(row);
            }
            if (!baseline && goal.Stage == EventSubGoalStage.Progress)
            {
                foreach (
                    var amount in sources
                        .Where(s =>
                            s.Event == GoalLifecycleKind.Milestone
                            && row.CurrentAmount < s.Milestone
                            && goal.CurrentAmount >= s.Milestone
                        )
                        .Select(s => s.Milestone)
                        .Distinct()
                )
                {
                    if (
                        await db.AutomationGoalMilestones.AnyAsync(
                            m =>
                                m.HostId == host.Id
                                && m.GoalId == goal.GoalId
                                && m.Amount == amount,
                            cancellation
                        )
                    )
                    {
                        continue;
                    }
                    _ = db.AutomationGoalMilestones.Add(
                        new()
                        {
                            HostId = host.Id,
                            GoalId = goal.GoalId,
                            Amount = amount,
                        }
                    );
                    crossed.Add(amount);
                }
            }
            row.CurrentAmount = goal.CurrentAmount;
            row.ObservedAtUtc = goal.Timestamp.UtcDateTime;
            row.ConnectionId = epoch;
            row.IsEnded = goal.Stage == EventSubGoalStage.End;
            _ = await db.SaveChangesAsync(cancellation);
            await transaction.CommitAsync(cancellation);
        }
        var kind = goal.Stage switch
        {
            EventSubGoalStage.Begin => GoalLifecycleKind.Started,
            EventSubGoalStage.Progress => GoalLifecycleKind.Progressed,
            EventSubGoalStage.End => GoalLifecycleKind.Ended,
        };
        _ = await EmitGoalAsync(host, goal, kind, 0, cancellation);
        foreach (var amount in crossed)
        {
            _ = await EmitGoalAsync(host, goal, GoalLifecycleKind.Milestone, amount, cancellation);
        }
    }

    private Task<AutomationDispatchOutcome> EmitGoalAsync(
        BotHost host,
        EventSubGoalEvent goal,
        GoalLifecycleKind kind,
        long amount,
        CancellationToken ct
    ) =>
        runtime.DispatchExpandedAsync(
            Create(
                host,
                AutomationDefinitionIds.GoalSource,
                kind == GoalLifecycleKind.Milestone
                    ? $"{goal.GoalId}:milestone:{amount}"
                    : goal.MessageId,
                goal.Timestamp,
                clock.GetUtcNow(),
                [
                    Text("goal-id", goal.GoalId),
                    Text("goal-type", goal.Type),
                    Text("event-kind", kind.ToString()),
                    Number("current-amount", goal.CurrentAmount),
                    Number("target-amount", goal.TargetAmount),
                    Boolean("achieved", goal.Achieved),
                ]
            ),
            c =>
                c is GoalSourceConfiguration s
                && s.Event == kind
                && (kind != GoalLifecycleKind.Milestone || s.Milestone == amount),
            ct
        );

    public async Task ChatSettingsChangedAsync(
        EventSubChatSettingsEvent change,
        CancellationToken cancellation
    )
    {
        var host = await HostAsync(change.BroadcasterId, HostFeatureFlags.None, cancellation);
        if (host is null)
        {
            return;
        }
        _ = await runtime.DispatchExpandedAsync(
            Create(
                host,
                AutomationDefinitionIds.ChatSettingsSource,
                change.MessageId,
                change.Timestamp,
                clock.GetUtcNow(),
                [
                    Boolean("slow-mode", change.Slow),
                    Number("slow-seconds", change.SlowSeconds),
                    Boolean("subscriber-mode", change.Subscribers),
                    Boolean("emote-mode", change.Emotes),
                    Boolean("follower-mode", change.Followers),
                    Number("follower-minutes", change.FollowerMinutes),
                    Boolean("unique-chat", change.UniqueChat),
                ]
            ),
            c => c is ChatSettingsSourceConfiguration,
            cancellation
        );
    }

    public async Task ModerationOccurredAsync(
        EventSubModerationEvent change,
        CancellationToken cancellation
    )
    {
        var host = await HostAsync(change.BroadcasterId, HostFeatureFlags.None, cancellation);
        if (
            host is null
            || !await AuthorizedAsync(host, EventSubModerationActions.ReadScopes, cancellation)
        )
        {
            return;
        }
        _ = await runtime.DispatchExpandedAsync(
            Create(
                host,
                AutomationDefinitionIds.ModerationSource,
                change.MessageId,
                change.Timestamp,
                clock.GetUtcNow(),
                [
                    Text("moderation-action", change.Action),
                    Text("source-channel-id", change.SourceBroadcasterId, true),
                    Text("source-channel-login", change.SourceBroadcasterLogin),
                    Text("subject-login", change.SubjectLogin),
                    Text("subject-name", change.SubjectName),
                    Number("duration-seconds", change.DurationSeconds),
                    Number("affected-count", change.AffectedCount),
                ],
                new(change.ModeratorId, change.ModeratorLogin, change.ModeratorName)
            ),
            c =>
                c is ModerationSourceConfiguration s
                && (s.Action == "any" || s.Action == change.Action),
            cancellation
        );
    }

    public async Task OutgoingRaidAsync(
        EventSubIncomingRaidEvent raid,
        CancellationToken cancellation
    )
    {
        if (raid.SubscriptionDirection != EventSubRaidSubscriptionDirection.Outgoing)
        {
            return;
        }
        var host = await HostAsync(
            raid.FromBroadcasterUserId,
            HostFeatureFlags.RaidCollaboration,
            cancellation
        );
        if (host is null)
        {
            return;
        }
        _ = await runtime.DispatchExpandedAsync(
            Create(
                host,
                AutomationDefinitionIds.OutgoingRaidSource,
                raid.MessageId,
                raid.MessageTimestamp,
                clock.GetUtcNow(),
                [Number("viewer-count", raid.ViewerCount)],
                new(
                    raid.ToBroadcasterUserId,
                    raid.ToBroadcasterUserLogin,
                    raid.ToBroadcasterUserName
                )
            ),
            c => c is OutgoingRaidSourceConfiguration,
            cancellation
        );
    }

    public async Task RedemptionChangedAsync(
        EventSubRewardRedemptionEvent change,
        CancellationToken cancellation
    )
    {
        if (
            change.IsNewRedemption
            || change.Status
                is not (
                    HelixRewardRedemptionStatus.Fulfilled
                    or HelixRewardRedemptionStatus.Canceled
                )
        )
        {
            return;
        }
        var host = await HostAsync(
            change.BroadcasterUserId,
            HostFeatureFlags.RewardsAndRedemptions,
            cancellation
        );
        if (host is null)
        {
            return;
        }
        var kind =
            change.Status == HelixRewardRedemptionStatus.Fulfilled
                ? RedemptionUpdateKind.Fulfilled
                : RedemptionUpdateKind.Cancelled;
        _ = await runtime.DispatchExpandedAsync(
            Create(
                host,
                AutomationDefinitionIds.RedemptionUpdateSource,
                $"{change.RedemptionId}:{kind}",
                change.MessageTimestamp ?? clock.GetUtcNow(),
                clock.GetUtcNow(),
                [
                    Text("redemption-id", change.RedemptionId, true),
                    Text("reward-id", change.RewardId, true),
                    Text("status", kind.ToString()),
                ],
                new(change.UserId, change.UserLogin, change.UserName)
            ),
            c =>
                c is RedemptionUpdateSourceConfiguration s
                && s.Event == kind
                && (s.RewardId is null || s.RewardId == change.RewardId),
            cancellation
        );
    }

    public async Task AdBreakStartedAsync(EventSubAdBreakEvent ad, CancellationToken cancellation)
    {
        var host = await HostAsync(ad.BroadcasterId, HostFeatureFlags.None, cancellation);
        if (
            host is null
            || !await AcceptEventAsync(host.Id, ad.Timestamp, cancellation)
            || !await AuthorizedAsync(host, ["channel:read:ads"], cancellation)
        )
        {
            return;
        }
        if (!(await SourcesAsync(host, cancellation)).OfType<AdTimingSourceConfiguration>().Any())
        {
            return;
        }
        lock (_observationGate)
        {
            if (_ads.TryGetValue(host.Id, out var previous) && previous.StartedAt >= ad.StartedAt)
            {
                return;
            }
            _ads[host.Id] = new(ad.StartedAt, ad.DurationSeconds, null, clock.GetUtcNow());
        }
        _ = await EmitAdAsync(
            host,
            AdTimingKind.Started,
            ad.StartedAt,
            ad.StartedAt.AddSeconds(ad.DurationSeconds),
            TimeSpan.Zero,
            false,
            cancellation
        );
    }

    private async Task PollAdsAsync(
        BotHost host,
        IReadOnlyList<AdTimingSourceConfiguration> sources,
        CancellationToken ct
    )
    {
        var status = await broadcasterTokens.GetTokenStatusAsync(host.Id, ["channel:read:ads"], ct);
        var token = status.Match<string?>(
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            r => r.AccessToken
        );
        if (token is null)
        {
            _ = _ads.TryRemove(host.Id, out _);
            _readiness[host.Id] =
                "Reconnect the broadcaster with channel:read:ads; no derived ad deadlines are active.";
            return;
        }
        HelixAdScheduleOutcome response;
        try
        {
            response = await helix.GetAdScheduleAsync(
                new(settings.Identity.ClientId, token),
                host.TwitchUserId ?? "",
                ct
            );
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Ad schedule unavailable for host {HostId} ({FailureType}).",
                host.Id,
                error.GetType().Name
            );
            response = new HelixAdScheduleOutcome.Unavailable();
        }
        if (response is not HelixAdScheduleOutcome.Available available)
        {
            _ = _ads.TryRemove(host.Id, out _);
            _readiness[host.Id] =
                "The Twitch ad schedule is unavailable. Reconnect/check channel eligibility; superseded deadlines are discarded.";
            return;
        }
        _ = _readiness.TryRemove(host.Id, out _);
        var now = clock.GetUtcNow();
        AdObservation observation;
        lock (_observationGate)
        {
            var prior = _ads.GetValueOrDefault(host.Id);
            var matches = prior is not null && available.Schedule.LastAdAt == prior.StartedAt;
            observation = matches
                ? prior! with
                {
                    NextAdAt = available.Schedule.NextAdAt,
                    ObservedAt = now,
                }
                : new(default, 0, available.Schedule.NextAdAt, now);
            _ads[host.Id] = observation;
        }
        foreach (var source in sources)
        {
            DateTimeOffset? deadline =
                source.Event == AdTimingKind.BeforeScheduled ? observation.NextAdAt
                : observation.Duration > 0 ? observation.StartedAt.AddSeconds(observation.Duration)
                : null;
            if (deadline is not { } due || source.Event == AdTimingKind.Started)
            {
                continue;
            }
            var trigger = source.Event == AdTimingKind.ExpectedFinish ? due : due - source.Offset;
            if (trigger > now || now - trigger > TimeSpan.FromSeconds(6))
            {
                continue;
            }
            _ = await EmitAdAsync(
                host,
                source.Event,
                trigger,
                due,
                source.Offset,
                true,
                ct,
                observation
            );
        }
    }

    private Task<AutomationDispatchOutcome> EmitAdAsync(
        BotHost host,
        AdTimingKind kind,
        DateTimeOffset at,
        DateTimeOffset deadline,
        TimeSpan offset,
        bool derived,
        CancellationToken ct,
        AdObservation? observation = null
    ) =>
        runtime.DispatchExpandedAsync(
            Create(
                host,
                AutomationDefinitionIds.AdTimingSource,
                $"{kind}:{deadline.UtcTicks}:{offset.Ticks}",
                at,
                clock.GetUtcNow(),
                [
                    Text("timing-kind", kind.ToString()),
                    Time("deadline", deadline),
                    Number(
                        "remaining-seconds",
                        (decimal)Math.Max(0, (deadline - clock.GetUtcNow()).TotalSeconds)
                    ),
                    Boolean("derived", derived),
                ]
            ),
            c =>
                c is AdTimingSourceConfiguration s
                && s.Event == kind
                && (
                    kind is AdTimingKind.Started or AdTimingKind.ExpectedFinish
                    || s.Offset == offset
                ),
            ct,
            observation is null
                ? null
                : (_, _) => Task.FromResult(_ads.GetValueOrDefault(host.Id) == observation)
        );
}
