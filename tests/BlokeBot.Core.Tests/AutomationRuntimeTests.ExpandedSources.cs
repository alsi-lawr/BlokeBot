using System.Net;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task CountdownActions_UseRealManualRun_ResetEpoch_CancelNormally_AndNeverCatchUpOnRestart()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var manual = Node("manual-run", """{"data":"go"}""");
        var start = Node("start-countdown", """{"name":"tea","duration-seconds":10}""");
        var flow = await f.SaveAsync([manual, start], [Edge(manual, "flow", start)]);
        foreach (var lifecycle in Enum.GetValues<CountdownLifecycle>())
        {
            _ = await SaveExpandedChatAsync(
                f,
                "countdown-lifecycle",
                $"{{\"name\":\"tea\",\"event\":\"{lifecycle}\",\"remaining-seconds\":5}}",
                lifecycle.ToString()
            );
        }
        var run = new AutomationManualRunService(f.Database, f.Runtime, f.Catalog, f.Clock);
        _ = (
            await run.RunAsync(new(f.HostId), flow, CancellationToken.None)
        ).ShouldBeOfType<AutomationManualRunOutcome.Dispatched>();
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["Started"]);
        _ = await run.RunAsync(new(f.HostId), flow, CancellationToken.None);
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await f.Countdowns.TickAsync(
            await HostForExpandedAsync(f),
            [new("tea", CountdownLifecycle.Remaining, TimeSpan.FromSeconds(5))],
            CancellationToken.None
        );
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Last().ShouldBe("Remaining");
        _ = await f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(20), CountdownOperation.Reset),
            CancellationToken.None
        );
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Last().ShouldBe("Reset");
        _ = await f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.Zero, CountdownOperation.Cancel),
            CancellationToken.None
        );
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Last().ShouldBe("Cancelled");
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.Countdowns.TickAsync(await HostForExpandedAsync(f), [], CancellationToken.None);
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(4);
        _ = await f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(2), CountdownOperation.Start),
            CancellationToken.None
        );
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        var before = f.Chat.Messages.Count;
        var restarted = new AutomationCountdownService(f.Database, f.Clock, () => f.Runtime);
        await restarted.InitializeAsync(CancellationToken.None);
        f.Clock.Advance(TimeSpan.FromSeconds(3));
        await restarted.TickAsync(
            await HostForExpandedAsync(f),
            [new("tea", CountdownLifecycle.Remaining, TimeSpan.FromSeconds(5))],
            CancellationToken.None
        );
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(before);
        await using var db = await f.Database.CreateDbContextAsync();
        var timer = await db.AutomationCountdowns.SingleAsync();
        timer.IsRunning.ShouldBeFalse();
        timer.WasCancelled.ShouldBeTrue();
    }

    [Test]
    [Arguments(CountdownOperation.Reset, false)]
    [Arguments(CountdownOperation.Reset, true)]
    [Arguments(CountdownOperation.Cancel, false)]
    [Arguments(CountdownOperation.Cancel, true)]
    public async Task Countdown_UpdateAtActualObservationAdmissionBoundary_FencesSupersededWarningAndFinish(
        CountdownOperation operation,
        bool finished
    )
    {
        var barrier = new AutomationDispatchTransactionInterleaving();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [barrier]);
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            "{\"name\":\"race\",\"event\":\"Remaining\",\"remaining-seconds\":5}",
            "old warning"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            "{\"name\":\"race\",\"event\":\"Finished\",\"remaining-seconds\":0}",
            "old finish"
        );
        _ = await f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("race", TimeSpan.FromSeconds(10), CountdownOperation.Start),
            CancellationToken.None
        );
        f.Clock.Advance(TimeSpan.FromSeconds(finished ? 10 : 5));
        barrier.Arm(finished && operation == CountdownOperation.Cancel ? 0 : 1);
        var tick = f.Countdowns.TickAsync(
            await HostForExpandedAsync(f),
            [new("race", CountdownLifecycle.Remaining, TimeSpan.FromSeconds(5))],
            CancellationToken.None
        );
        await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            _ = await f.Countdowns.ApplyAsync(
                new(f.HostId),
                new(
                    "race",
                    operation == CountdownOperation.Reset
                        ? TimeSpan.FromSeconds(20)
                        : TimeSpan.Zero,
                    operation
                ),
                CancellationToken.None
            );
        }
        finally
        {
            barrier.Release();
        }
        await tick.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task CountdownThreshold_IsFutureCrossingOnly_AndFinishIsAdmittedOnce()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"short","event":"Remaining","remaining-seconds":5}""",
            "warning"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"short","event":"Finished","remaining-seconds":0}""",
            "finished"
        );
        _ = await f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("short", TimeSpan.FromSeconds(2), CountdownOperation.Start),
            CancellationToken.None
        );
        f.Clock.Advance(TimeSpan.FromSeconds(3));
        var host = await HostForExpandedAsync(f);
        await f.Countdowns.TickAsync(
            host,
            [new("short", CountdownLifecycle.Remaining, TimeSpan.FromSeconds(5))],
            CancellationToken.None
        );
        await f.Countdowns.TickAsync(
            host,
            [new("short", CountdownLifecycle.Remaining, TimeSpan.FromSeconds(5))],
            CancellationToken.None
        );
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["finished"]);
    }

    [Test]
    public async Task ManualRun_IsHostScoped_SavedEnabledOnly_AndCarriesExplicitRealData()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"explicit"}""");
        var action = Node("send-chat", """{"message":"Fallback"}""") with
        {
            InputBindings = Bindings("message", AutomationInputBindingMode.Connected),
        };
        var flow = await f.SaveAsync(
            [source, action],
            [
                Edge(source, "flow", action),
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Data,
                    source.Id,
                    new("manual-data"),
                    action.Id,
                    new("message")
                ),
            ]
        );
        var run = new AutomationManualRunService(f.Database, f.Runtime, f.Catalog, f.Clock);
        _ = (
            await run.RunAsync(new(f.HostId + 1000), flow, CancellationToken.None)
        ).ShouldBeOfType<AutomationManualRunOutcome.Unavailable>();
        f.Chat.Messages.ShouldBeEmpty();
        _ = await run.RunAsync(new(f.HostId), flow, CancellationToken.None);
        f.Chat.Messages.Single().ShouldBe("explicit");
        _ = await f.Flows.SetEnabledAsync(new(f.HostId), flow, false, CancellationToken.None);
        _ = (
            await run.RunAsync(new(f.HostId), flow, CancellationToken.None)
        ).ShouldBeOfType<AutomationManualRunOutcome.Unavailable>();
        f.Chat.Messages.Count.ShouldBe(1);
    }

    [Test]
    public void ScheduledTime_UsesNamedZone_SkipsGap_ChoosesFirstFold_AndMaintainsFixedCadence()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        AutomationScheduleTime.ResolveLocal(new(2026, 3, 29, 1, 30, 0), zone).ShouldBeNull();
        AutomationScheduleTime
            .ResolveLocal(new(2026, 10, 25, 1, 30, 0), zone)
            .ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
        var weekly = new ScheduledTimeSourceConfiguration(
            ScheduledTimeKind.Weekly,
            zone.Id,
            new(2026, 1, 1, 1, 30, 0),
            DayOfWeek.Sunday,
            TimeSpan.FromMinutes(1),
            false
        );
        AutomationScheduleTime
            .Next(weekly, new(2026, 3, 28, 0, 0, 0, TimeSpan.Zero))
            .ShouldBe(new DateTimeOffset(2026, 4, 5, 0, 30, 0, TimeSpan.Zero));
        var interval = weekly with
        {
            Kind = ScheduledTimeKind.Interval,
            Zone = "UTC",
            LocalTime = new(2026, 1, 1, 0, 0, 0),
            Interval = TimeSpan.FromMinutes(10),
        };
        AutomationScheduleTime
            .Next(interval, new(2026, 1, 1, 0, 34, 0, TimeSpan.Zero))
            .ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 40, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Schedule_InvalidIntervalAnchor_SkipsOriginalCadenceSlots_ThenRunsFixedUtcWithoutCatchUp()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var beforeGap = new DateTimeOffset(2026, 3, 29, 0, 20, 0, TimeSpan.Zero);
        f.Clock.Advance(beforeGap - f.Clock.GetUtcNow());
        _ = await SaveExpandedChatAsync(
            f,
            "scheduled-time",
            """{"kind":"Interval","zone":"Europe/London","local-time":"2026-03-29T01:30:00","day":"Sunday","interval-seconds":600,"live-only":"false"}""",
            "scheduled"
        );
        var x = ExpandedFixtureFor(f);
        await x.Runtime.TickAsync(CancellationToken.None);
        foreach (var minute in new[] { 30, 40, 50 })
        {
            f.Clock.Advance(
                new DateTimeOffset(2026, 3, 29, 0, minute, 0, TimeSpan.Zero) - f.Clock.GetUtcNow()
            );
            await x.Runtime.TickAsync(CancellationToken.None);
            f.Chat.Messages.ShouldBeEmpty();
        }
        f.Clock.Advance(TimeSpan.FromMinutes(10));
        await x.Runtime.TickAsync(CancellationToken.None);
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["scheduled"]);
        f.Clock.Advance(TimeSpan.FromMinutes(10));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(2);
        var restarted = ExpandedFixtureFor(f);
        f.Clock.Advance(TimeSpan.FromMinutes(25));
        await restarted.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(2);
        f.Clock.Advance(TimeSpan.FromMinutes(5));
        await restarted.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(3);
        await using var db = await f.Database.CreateDbContextAsync();
        var occurrences = await db
            .AutomationFlowRuns.OrderBy(r => r.StartedAtUtc)
            .Select(r => r.ContextJson)
            .ToArrayAsync();
        occurrences
            .Select(json =>
                AutomationRuntimeSerialization
                    .RestoreContext(AutomationContextSchema.CurrentVersion, json)
                    .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
                    .Context.Timestamps.OccurredAtUtc.UtcDateTime
            )
            .ShouldBe([
                new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 29, 1, 10, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 29, 1, 40, 0, DateTimeKind.Utc),
            ]);
    }

    [Test]
    public async Task Schedule_MissedOrOfflineOccurrenceIsSkipped_NotReplayed_AndLiveOnlyIsConfirmed()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "scheduled-time",
            """{"kind":"Interval","zone":"UTC","local-time":"2026-08-03T12:00:00","day":"Monday","interval-seconds":10,"live-only":"true"}""",
            "scheduled"
        );
        var x = ExpandedFixtureFor(f);
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        x.Streams.Current = new HostStreamLivenessOutcome.Offline();
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBeEmpty();
        x.Streams.Current = new HostStreamLivenessOutcome.Live(
            "stream",
            f.Clock.GetUtcNow() - TimeSpan.FromMinutes(1)
        );
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(9));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(1);
        x.Connection.Current = new BotRuntimeStatus.Authorized();
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        await x.Runtime.TickAsync(CancellationToken.None);
        x.Connection.Current = new BotRuntimeStatus.Connected(["streamer"]);
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(1);
        x.Streams.Current = new HostStreamLivenessOutcome.Unavailable(
            HostStreamLivenessUnavailableReason.ProviderRequestFailed,
            new HttpRequestException()
        );
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(1);
        x.Streams.Current = new HostStreamLivenessOutcome.Live(
            "stream",
            f.Clock.GetUtcNow() - TimeSpan.FromMinutes(2)
        );
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(1);
    }

    [Test]
    public async Task Uptime_LateThresholdIsOncePerRealStream_IntervalsDoNotCatchUp()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "stream-uptime",
            """{"kind":"Threshold","duration-seconds":30}""",
            "threshold"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "stream-uptime",
            """{"kind":"Interval","duration-seconds":10}""",
            "tick"
        );
        var x = ExpandedFixtureFor(f);
        x.Streams.Current = new HostStreamLivenessOutcome.Live(
            "actual",
            f.Clock.GetUtcNow() - TimeSpan.FromSeconds(35)
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["threshold"]);
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Last().ShouldBe("tick");
        var restarted = ExpandedFixtureFor(f);
        restarted.Streams.Current = x.Streams.Current;
        await restarted.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(2);
        f.Clock.Advance(TimeSpan.FromSeconds(50));
        await restarted.Runtime.TickAsync(CancellationToken.None);
        // A current on-time interval can fire, never the missed four intervals.
        f.Chat.Messages.Count.ShouldBe(3);
        restarted.Streams.Current = new HostStreamLivenessOutcome.Live(
            "stale-other-stream",
            f.Clock.GetUtcNow() - TimeSpan.FromMinutes(10)
        );
        await restarted.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(3);
    }

    [Test]
    public async Task Chat_FirstObservedSurvivesRestart_EndsWithStream_ExcludesBot_AndMatchesProviderEmoteIds()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"Emote","match":"25"}""",
            "emote"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"Keyword","match":"tea"}""",
            "word"
        );
        var x = ExpandedFixtureFor(f);
        x.Streams.Current = new HostStreamLivenessOutcome.Live(
            "actual",
            f.Clock.GetUtcNow() - TimeSpan.FromMinutes(1)
        );
        EventSubObservedChatEvent message = new(
            "chat1",
            f.Clock.GetUtcNow(),
            "streamer-id",
            "streamer-id",
            "viewer-id",
            "viewer",
            "Viewer",
            "TEA!",
            ["25"]
        );
        await x.Runtime.ChatReceivedAsync(message, CancellationToken.None);
        f.Chat.Messages.Order().ShouldBe(new[] { "first", "emote", "word" }.Order());
        var restarted = ExpandedFixtureFor(f);
        restarted.Streams.Current = x.Streams.Current;
        await restarted.Runtime.ChatReceivedAsync(
            message with
            {
                MessageId = "chat2",
                Text = "Kappa",
                EmoteIds = [],
            },
            CancellationToken.None
        );
        f.Chat.Messages.Count.ShouldBe(3);
        await restarted.Runtime.ChatReceivedAsync(
            message with
            {
                MessageId = "unseen-after-restart",
                ViewerId = "viewer-b",
                ViewerLogin = "viewerb",
                ViewerName = "Viewer B",
                Text = "hello",
                EmoteIds = [],
            },
            CancellationToken.None
        );
        f.Chat.Messages.Last().ShouldBe("first");
        f.Chat.Messages.Count.ShouldBe(4);
        await restarted.Runtime.ChatReceivedAsync(
            message with
            {
                MessageId = "bot",
                ViewerLogin = "bot",
                ViewerId = "bot-id",
            },
            CancellationToken.None
        );
        f.Chat.Messages.Count.ShouldBe(4);
        await restarted.Runtime.StreamEndedAsync(
            new("offline", f.Clock.GetUtcNow(), "streamer-id", "streamer", "Streamer"),
            CancellationToken.None
        );
        restarted.Streams.Current = new HostStreamLivenessOutcome.Live("next", f.Clock.GetUtcNow());
        await restarted.Runtime.ChatReceivedAsync(
            message with
            {
                MessageId = "chat3",
                Text = "steam",
                EmoteIds = [],
            },
            CancellationToken.None
        );
        f.Chat.Messages.Last().ShouldBe("first");
        f.Chat.Messages.Count.ShouldBe(5);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationSeenViewers.SingleAsync()).ViewerId.ShouldBe("viewer-id");
        ExpandedAutomationRuntime.KeywordMatches("steaming tea_thing", "tea").ShouldBeFalse();
    }

    [Test]
    public async Task Chat_PhraseMatching_IsLiteralContiguousAndCaseInsensitive_WithoutSharedChannelLeakage()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            "{\"kind\":\"Phrase\",\"match\":\"hello tea\"}",
            "phrase"
        );
        var runtime = ExpandedFixtureFor(f).Runtime;
        var message = new EventSubObservedChatEvent(
            "positive",
            f.Clock.GetUtcNow(),
            "streamer-id",
            "streamer-id",
            "viewer",
            "viewer",
            "Viewer",
            "Well HELLO TEA!",
            []
        );
        await runtime.ChatReceivedAsync(message, CancellationToken.None);
        await runtime.ChatReceivedAsync(
            message with
            {
                MessageId = "negative",
                Text = "hello lovely tea",
            },
            CancellationToken.None
        );
        await runtime.ChatReceivedAsync(
            message with
            {
                MessageId = "shared",
                SourceBroadcasterId = "remote-host",
            },
            CancellationToken.None
        );
        f.Chat.Messages.ShouldBe(["phrase"]);
    }

    [Test]
    public async Task Goal_MilestonesAreObservedCrossings_OncePerIdentity_AcrossDecreaseAndRecovery()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Milestone","milestone":10}""",
            "goal"
        );
        var x = ExpandedFixtureFor(f);
        async Task Send(int value, string id, ExpandedAutomationRuntime r, string goalId = "goal-1")
        {
            f.Clock.Advance(TimeSpan.FromSeconds(1));
            await r.GoalChangedAsync(
                new(
                    id,
                    f.Clock.GetUtcNow(),
                    "streamer-id",
                    goalId,
                    "follower",
                    EventSubGoalStage.Progress,
                    value,
                    100,
                    false
                ),
                CancellationToken.None
            );
        }
        await Send(11, "baseline", x.Runtime);
        f.Chat.Messages.ShouldBeEmpty();
        await Send(8, "down", x.Runtime);
        await Send(10, "cross", x.Runtime);
        await Send(5, "down2", x.Runtime);
        await Send(12, "cross2", x.Runtime);
        f.Chat.Messages.Count.ShouldBe(1);
        var restarted = ExpandedFixtureFor(f);
        await Send(2, "recovered", restarted.Runtime);
        await Send(20, "cross3", restarted.Runtime);
        f.Chat.Messages.Count.ShouldBe(1);
        await restarted.Runtime.GoalChangedAsync(
            new(
                "stale-down",
                f.Clock.GetUtcNow().AddMinutes(-1),
                "streamer-id",
                "goal-1",
                "follower",
                EventSubGoalStage.Progress,
                0,
                100,
                false
            ),
            CancellationToken.None
        );
        await Send(11, "goal2-baseline", restarted.Runtime, "goal-2");
        await Send(8, "goal2-down", restarted.Runtime, "goal-2");
        await Send(10, "goal2-cross", restarted.Runtime, "goal-2");
        await Send(5, "goal2-down-again", restarted.Runtime, "goal-2");
        await Send(12, "goal2-recross", restarted.Runtime, "goal-2");
        f.Chat.Messages.ShouldBe(["goal", "goal"]);
        await using var db = await f.Database.CreateDbContextAsync();
        (
            await db
                .AutomationGoalMilestones.OrderBy(m => m.GoalId)
                .Select(m => m.GoalId)
                .ToArrayAsync()
        ).ShouldBe(["goal-1", "goal-2"]);
        (
            await db.AutomationGoalObservations.SingleAsync(g => g.GoalId == "goal-1")
        ).CurrentAmount.ShouldBe(20);
    }

    [Test]
    public async Task Metadata_IsChangedFieldFiltered_AndIgnoresBaselineDuplicatesAndOlderUpdates()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "channel-metadata-changed",
            """{"field":"Title"}""",
            "title"
        );
        var x = ExpandedFixtureFor(f);
        var at = f.Clock.GetUtcNow();
        EventSubChannelUpdateEvent update = new(
            "base",
            at,
            "streamer-id",
            "streamer",
            "Streamer",
            "1",
            "One",
            "old"
        );
        await x.Runtime.MetadataChangedAsync(update, CancellationToken.None);
        await x.Runtime.MetadataChangedAsync(
            update with
            {
                MessageId = "category",
                MessageTimestamp = at.AddSeconds(1),
                CategoryId = "2",
            },
            CancellationToken.None
        );
        await x.Runtime.MetadataChangedAsync(
            update with
            {
                MessageId = "title",
                MessageTimestamp = at.AddSeconds(2),
                StreamTitle = "new",
                CategoryId = "2",
            },
            CancellationToken.None
        );
        await x.Runtime.MetadataChangedAsync(
            update with
            {
                MessageId = "older",
                MessageTimestamp = at.AddSeconds(1),
            },
            CancellationToken.None
        );
        f.Chat.Messages.ShouldBe(["title"]);
    }

    [Test]
    public async Task Ads_SupersededScheduleCannotFire_ObservedDurationOwnsRemainingAndDerivedEnd()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "ad-timing",
            """{"event":"BeforeScheduled","offset-seconds":5}""",
            "before"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "ad-timing",
            """{"event":"Started","offset-seconds":0}""",
            "started"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "ad-timing",
            """{"event":"ExpectedFinish","offset-seconds":0}""",
            "end"
        );
        var x = ExpandedFixtureFor(f);
        var now = f.Clock.GetUtcNow();
        x.Http.Body = AdScheduleJson(now.AddSeconds(10), null, 30);
        await x.Runtime.TickAsync(CancellationToken.None);
        x.Http.Body = AdScheduleJson(now.AddMinutes(1), null, 30);
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBeEmpty();
        await x.Runtime.AdBreakStartedAsync(
            new("ad", f.Clock.GetUtcNow(), "streamer-id", f.Clock.GetUtcNow(), 10, false),
            CancellationToken.None
        );
        var start = f.Clock.GetUtcNow();
        x.Http.Body = AdScheduleJson(now.AddMinutes(1), start, 180);
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["started", "end"]);
        x.Http.Code = HttpStatusCode.Forbidden;
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await x.Runtime.TickAsync(CancellationToken.None);
        _ = x.Runtime.UnavailableReason(f.HostId).ShouldNotBeNull();
        x.Http.Code = HttpStatusCode.OK;
        x.Http.Body = AdScheduleJson(null, null, 0);
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(2);
    }

    [Test]
    public async Task FeatureAndCueLifecycle_UseExistingHostOwners_GatesAndHonestPlaybackOutcomes()
    {
        await using var f = await RuntimeFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.Points
                | HostFeatureFlags.Guessing
                | HostFeatureFlags.PlayWithViewers
                | HostFeatureFlags.Overlays
        );
        var x = ExpandedFixtureFor(f);
        foreach (var kind in Enum.GetValues<FeatureLifecycleKind>())
        {
            var definition =
                kind.ToString().StartsWith("Giveaway", StringComparison.Ordinal)
                    ? "giveaway-lifecycle"
                : kind.ToString().StartsWith("Guessing", StringComparison.Ordinal)
                    ? "guessing-lifecycle"
                : "queue-lifecycle";
            _ = await SaveExpandedChatAsync(
                f,
                definition,
                $"{{\"event\":\"{kind}\"}}",
                kind.ToString()
            );
            await x.Runtime.FeatureAsync(
                f.HostId,
                kind,
                "feature",
                "committed",
                f.Clock.GetUtcNow(),
                "public",
                CancellationToken.None
            );
        }
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(7);
        _ = await SaveExpandedChatAsync(f, "cue-lifecycle", """{"event":"Finished"}""", "cue");
        var observer = new AutomationCueLifecycleObserver(f.Database, () => f.Runtime, f.Clock);
        await observer.CueChangedAsync(
            new(
                f.HostId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                OverlayCueLifecycleKind.Finished,
                OverlayCueLifecycleOutcome.TimeDerivedEndUnconfirmed,
                f.Clock.GetUtcNow()
            ),
            CancellationToken.None
        );
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Last().ShouldBe("cue");
        await using var db = await f.Database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync();
        host.EnabledFeatures &= ~HostFeatureFlags.Automations;
        _ = await db.SaveChangesAsync();
        await x.Runtime.FeatureAsync(
            f.HostId,
            FeatureLifecycleKind.QueueJoined,
            "feature",
            "new",
            f.Clock.GetUtcNow(),
            "",
            CancellationToken.None
        );
        f.Chat.Messages.Count.ShouldBe(8);
    }

    [Test]
    public async Task ModerationSettingsOutgoingRaidAndTerminalRedemptions_StayDistinctAndHostScoped()
    {
        await using var f = await RuntimeFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.RaidCollaboration
                | HostFeatureFlags.RewardsAndRedemptions
        );
        _ = await SaveExpandedChatAsync(
            f,
            "moderation-action",
            """{"action":"shared_chat_ban"}""",
            "moderated"
        );
        _ = await SaveExpandedChatAsync(f, "chat-settings-changed", "{}", "settings");
        _ = await SaveExpandedChatAsync(f, "outgoing-raid", "{}", "raid");
        _ = await SaveExpandedChatAsync(
            f,
            "redemption-updated",
            """{"event":"Fulfilled"}""",
            "fulfilled"
        );
        var x = ExpandedFixtureFor(f);
        var now = f.Clock.GetUtcNow();
        await x.Runtime.ModerationOccurredAsync(
            new(
                "mod",
                now,
                "streamer-id",
                "remote",
                "remote",
                "shared_chat_ban",
                "moderator",
                "mod",
                "Mod",
                "subject",
                "Subject",
                0,
                0
            ),
            CancellationToken.None
        );
        await x.Runtime.ChatSettingsChangedAsync(
            new("settings", now, "streamer-id", true, 30, false, false, false, 0, true),
            CancellationToken.None
        );
        EventSubIncomingRaidEvent raid = new(
            "raid",
            now,
            "streamer-id",
            "streamer",
            "Streamer",
            "other-id",
            "other",
            "Other",
            10,
            EventSubRaidSubscriptionDirection.Outgoing
        );
        await x.Runtime.OutgoingRaidAsync(
            raid with
            {
                SubscriptionDirection = EventSubRaidSubscriptionDirection.Incoming,
            },
            CancellationToken.None
        );
        await x.Runtime.OutgoingRaidAsync(raid, CancellationToken.None);
        EventSubRewardRedemptionEvent redemption = new(
            "streamer-id",
            "streamer",
            "r1",
            "reward",
            "Reward",
            100,
            "viewer-id",
            "viewer",
            "Viewer",
            "private",
            HelixRewardRedemptionStatus.Fulfilled,
            now,
            "redemption",
            false
        );
        await x.Runtime.RedemptionChangedAsync(
            redemption with
            {
                IsNewRedemption = true,
            },
            CancellationToken.None
        );
        await x.Runtime.RedemptionChangedAsync(redemption, CancellationToken.None);
        await x.Runtime.RedemptionChangedAsync(redemption, CancellationToken.None);
        f.Chat.Messages.ShouldBe(["moderated", "settings", "raid", "fulfilled"]);
        await using var db = await f.Database.CreateDbContextAsync();
        var contexts = await db.AutomationFlowRuns.Select(r => r.ContextJson).ToArrayAsync();
        string.Join("\n", contexts).ShouldNotContain("private");
    }

    [Test]
    public async Task SelectedObservationSources_RequestOnlyTheirExtraReadGrants_AndReenableRejectsSuppressedEvents()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(f, "moderation-action", """{"action":"warn"}""", "warn");
        var x = ExpandedFixtureFor(f);
        var readiness = new TwitchEventSourceReadinessService(
            f.Database,
            f.Catalog,
            f.Runtime,
            x.Tokens,
            x.Runtime
        );
        var scopes = await readiness.AuthorizationScopesAsync(f.HostId, CancellationToken.None);
        scopes.ShouldContain("moderator:read:warnings");
        scopes.ShouldNotContain("channel:read:ads");
        scopes.ShouldNotContain("channel:read:goals");
        scopes
            .Except(HostBroadcasterAuthorizationService.MilestoneScopes)
            .ShouldNotContain(s => s.Contains(":manage:", StringComparison.Ordinal));
        var requirements = await x.Runtime.GetRequirementsAsync("streamer", CancellationToken.None);
        requirements.Single().ShouldBe(new EventSubExactSubscription("channel.moderate", "2"));
        var features = TestHostFeatureServices.Create(
            f.Database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [x.Runtime],
            f.Clock
        );
        _ = await features.DisableAsync(
            f.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        (await x.Runtime.GetRequirementsAsync("streamer", CancellationToken.None)).ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        _ = await features.EnableAsync(
            f.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        await x.Runtime.ModerationOccurredAsync(
            new(
                "suppressed",
                f.Clock.GetUtcNow().AddSeconds(-1),
                "streamer-id",
                "streamer-id",
                "streamer",
                "warn",
                "mod",
                "mod",
                "Mod",
                "viewer",
                "Viewer",
                0,
                0
            ),
            CancellationToken.None
        );
        f.Chat.Messages.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await x.Runtime.ModerationOccurredAsync(
            new(
                "current",
                f.Clock.GetUtcNow(),
                "streamer-id",
                "streamer-id",
                "streamer",
                "warn",
                "mod",
                "mod",
                "Mod",
                "viewer",
                "Viewer",
                0,
                0
            ),
            CancellationToken.None
        );
        f.Chat.Messages.ShouldBe(["warn"]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task FeatureReenable_CancelsCountdownQuietly_SkipsSuppressedScheduleAndUptime_ResumesFutureSlots(
        bool observedBeforeDisable
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"tea","event":"Cancelled","remaining-seconds":0}""",
            "cancelled"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "scheduled-time",
            """{"kind":"Interval","zone":"UTC","local-time":"2026-08-03T12:00:00","day":"Monday","interval-seconds":10,"live-only":"false"}""",
            "scheduled"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "stream-uptime",
            """{"kind":"Threshold","duration-seconds":30}""",
            "uptime"
        );
        var x = ExpandedFixtureFor(f);
        if (observedBeforeDisable)
        {
            x.Streams.Current = new HostStreamLivenessOutcome.Live(
                "current",
                f.Clock.GetUtcNow() - TimeSpan.FromSeconds(20)
            );
        }
        await x.Runtime.TickAsync(CancellationToken.None);
        _ = await f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("tea", TimeSpan.FromSeconds(20), CountdownOperation.Start),
            CancellationToken.None
        );
        var features = TestHostFeatureServices.Create(
            f.Database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [x.Runtime],
            f.Clock
        );
        _ = await features.DisableAsync(
            f.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        f.Clock.Advance(TimeSpan.FromSeconds(35));
        await x.Runtime.TickAsync(CancellationToken.None);
        if (!observedBeforeDisable)
        {
            x.Streams.Current = new HostStreamLivenessOutcome.Live(
                "new-while-disabled",
                f.Clock.GetUtcNow() - TimeSpan.FromSeconds(30)
            );
        }
        _ = await features.EnableAsync(
            f.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["scheduled"]);
        await using var db = await f.Database.CreateDbContextAsync();
        var timer = await db.AutomationCountdowns.SingleAsync();
        timer.IsRunning.ShouldBeFalse();
        timer.WasCancelled.ShouldBeTrue();
        (await db.AutomationFlows.CountAsync()).ShouldBe(3);
    }

    [Test]
    public async Task ParentFeatureDisable_DropsLifecycleObservationsAndInvalidatesPendingEffects_WithoutLosingFlows()
    {
        await using var f = await RuntimeFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.Points
                | HostFeatureFlags.Overlays
        );
        _ = await SaveExpandedChatAsync(
            f,
            "giveaway-lifecycle",
            """{"event":"GiveawayOpened"}""",
            "giveaway"
        );
        _ = await SaveExpandedChatAsync(f, "cue-lifecycle", """{"event":"Started"}""", "cue");
        var x = ExpandedFixtureFor(f);
        var observer = new AutomationCueLifecycleObserver(f.Database, () => f.Runtime, f.Clock);
        async Task Observe(string occurrence)
        {
            await x.Runtime.FeatureAsync(
                f.HostId,
                FeatureLifecycleKind.GiveawayOpened,
                "giveaway",
                occurrence,
                f.Clock.GetUtcNow(),
                "",
                CancellationToken.None
            );
            await observer.CueChangedAsync(
                new(
                    f.HostId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    OverlayCueLifecycleKind.Started,
                    OverlayCueLifecycleOutcome.ServerStartedUnconfirmed,
                    f.Clock.GetUtcNow()
                ),
                CancellationToken.None
            );
        }
        await Observe("admitted");
        var features = TestHostFeatureServices.Create(
            f.Database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [x.Runtime],
            f.Clock
        );
        _ = await features.DisableAsync(f.HostId, HostFeatureFlags.Points, CancellationToken.None);
        _ = await features.DisableAsync(
            f.HostId,
            HostFeatureFlags.Overlays,
            CancellationToken.None
        );
        await Observe("suppressed");
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBeEmpty();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(2);
            (
                await db.AutomationFlowRuns.AllAsync(r =>
                    r.Status == AutomationFlowRunStatus.Invalidated
                )
            ).ShouldBeTrue();
        }
        _ = await features.EnableAsync(f.HostId, HostFeatureFlags.Points, CancellationToken.None);
        _ = await features.EnableAsync(f.HostId, HostFeatureFlags.Overlays, CancellationToken.None);
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await Observe("fresh");
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Order(StringComparer.Ordinal).ShouldBe(["cue", "giveaway"]);
        await using var retained = await f.Database.CreateDbContextAsync();
        (await retained.AutomationFlows.CountAsync()).ShouldBe(2);
        (await retained.AutomationFlowRuns.CountAsync()).ShouldBe(4);
    }

    [Test]
    public async Task UptimeThreshold_CurrentStreamDoesNotRetriggerAfterTraceRetentionCleanup()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "stream-uptime",
            """{"kind":"Threshold","duration-seconds":30}""",
            "threshold"
        );
        var x = ExpandedFixtureFor(f);
        x.Streams.Current = new HostStreamLivenessOutcome.Live(
            "marathon",
            f.Clock.GetUtcNow() - TimeSpan.FromSeconds(35)
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["threshold"]);
        f.Clock.Advance(AutomationTraceStore.Retention + TimeSpan.FromMinutes(1));
        (
            await new AutomationTraceStore(f.Database, f.Clock).DeleteExpiredAsync(
                CancellationToken.None
            )
        ).ShouldBeGreaterThan(0);
        await new TwitchEventAutomationRuntime(
            f.Database,
            f.Runtime,
            f.Clock,
            NullLogger<TwitchEventAutomationRuntime>.Instance
        ).CleanupExpiredReceiptsAsync(CancellationToken.None);
        var restarted = ExpandedFixtureFor(f);
        restarted.Streams.Current = x.Streams.Current;
        await restarted.Runtime.TickAsync(CancellationToken.None);
        await restarted.Runtime.TickAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["threshold"]);
    }

    private static async Task<AutomationFlowId> SaveExpandedChatAsync(
        RuntimeFixture f,
        string type,
        string json,
        string reply
    )
    {
        var source = Node(type, json);
        var action = Node("send-chat", JsonSerializer.Serialize(new { message = reply }));
        return await f.SaveAsync([source, action], [Edge(source, "flow", action)]);
    }

    private static async Task<BotHost> HostForExpandedAsync(RuntimeFixture f)
    {
        await using var db = await f.Database.CreateDbContextAsync();
        return await db.Hosts.AsNoTracking().SingleAsync();
    }

    private static string AdScheduleJson(
        DateTimeOffset? next,
        DateTimeOffset? last,
        int duration
    ) =>
        JsonSerializer.Serialize(
            new
            {
                data = new[]
                {
                    new
                    {
                        next_ad_at = next?.ToString("O") ?? "",
                        last_ad_at = last?.ToString("O") ?? "",
                        duration,
                    },
                },
            }
        );

    private static ExpandedFixture ExpandedFixtureFor(RuntimeFixture f)
    {
        var streams = new ExpandedStreams();
        var tokens = new ExpandedTokens();
        var http = new ExpandedHttp();
        var connection = new ExpandedConnection();
        return new(
            new(
                f.Database,
                f.Runtime,
                f.Catalog,
                f.Countdowns,
                f.Clock,
                streams,
                new ExpandedAccounts(),
                tokens,
                new HelixClient(http, TwitchEndpointPolicy.Default),
                BotSettings.FromOptions(new BotOptions()),
                connection,
                NullLogger<ExpandedAutomationRuntime>.Instance
            ),
            streams,
            tokens,
            http,
            connection
        );
    }

    internal static ExpandedAutomationRuntime ObservationRuntimeFor(
        IDbContextFactory<BlokeBotDbContext> database,
        AutomationRuntimeService runtime,
        AutomationCatalogService catalog,
        TimeProvider clock,
        IHostBroadcasterTokenStatusProvider tokens
    ) =>
        new(
            database,
            runtime,
            catalog,
            new AutomationCountdownService(database, clock, () => runtime),
            clock,
            new ExpandedStreams(),
            new ExpandedAccounts(),
            tokens,
            new HelixClient(new ExpandedHttp(), TwitchEndpointPolicy.Default),
            BotSettings.FromOptions(new BotOptions()),
            new ExpandedConnection(),
            NullLogger<ExpandedAutomationRuntime>.Instance
        );

    private sealed record ExpandedFixture(
        ExpandedAutomationRuntime Runtime,
        ExpandedStreams Streams,
        ExpandedTokens Tokens,
        ExpandedHttp Http,
        ExpandedConnection Connection
    );

    private sealed class ExpandedStreams : IHostStreamLivenessProvider
    {
        internal HostStreamLivenessOutcome Current { get; set; } =
            new HostStreamLivenessOutcome.Offline();

        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string login) =>
            IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(Result<HostStreamLivenessOutcome, Never>.Success(Current))
            );
    }

    private sealed class ExpandedTokens : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int host,
            IEnumerable<string?> scopes,
            CancellationToken ct
        ) =>
            Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "token",
                    new("streamer-id", "streamer", OAuthScopeSet.Empty),
                    [.. scopes.OfType<string>()],
                    [.. scopes.OfType<string>()]
                )
            );

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(string login) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(new(login, "token"))
                )
            );
    }

    private sealed class ExpandedAccounts : IBotAccountProvider
    {
        public IO<BotAccount, AccessTokenUnavailableReason> GetBotAccount(string login) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(new("bot", "token"))
                )
            );
    }

    private sealed class ExpandedConnection : IBotRuntimeStatusAccessor
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }
        public BotRuntimeStatus Current { get; set; } =
            new BotRuntimeStatus.Connected(["streamer"]);
    }

    private sealed class ExpandedHttp : IHttpClientFactory
    {
        internal string Body { get; set; } = "{}";
        internal HttpStatusCode Code { get; set; } = HttpStatusCode.OK;

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(ExpandedHttp owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken ct
            ) =>
                Task.FromResult(
                    new HttpResponseMessage(owner.Code) { Content = new StringContent(owner.Body) }
                );
        }
    }
}
