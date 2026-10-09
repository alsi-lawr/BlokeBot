using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(
        OverlayCueLifecycleOutcome.TimeDerivedEndUnconfirmed,
        OverlayCueLifecycleKind.Finished,
        "time-derived-end-unconfirmed"
    )]
    [Arguments(
        OverlayCueLifecycleOutcome.BrowserReportedEndUnverified,
        OverlayCueLifecycleKind.Finished,
        "browser-reported-end-unverified"
    )]
    public async Task TypedCueOutcome_RetainsExactPublicContextAndOwnerIdentities_WithoutDuplicateEffects(
        OverlayCueLifecycleOutcome outcome,
        OverlayCueLifecycleKind kind,
        string label
    )
    {
        await using var f = await RuntimeFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.Automations | HostFeatureFlags.Overlays
        );
        _ = await SaveExpandedChatAsync(
            f,
            "cue-lifecycle",
            JsonSerializer.Serialize(new { @event = kind.ToString() }),
            "cue"
        );
        var observer = new AutomationCueLifecycleObserver(f.Database, () => f.Runtime, f.Clock);
        var notice = new OverlayCueLifecycleNotice(
            f.HostId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            kind,
            outcome,
            f.Clock.GetUtcNow()
        );
        await observer.CueChangedAsync(notice, CancellationToken.None);
        await observer.CueChangedAsync(notice, CancellationToken.None);
        f.Chat.Messages.ShouldBeEmpty();
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["cue"]);
        await using var db = await f.Database.CreateDbContextAsync();
        var run = await db.AutomationFlowRuns.SingleAsync();
        var context = AutomationRuntimeSerialization
            .RestoreContext(AutomationContextSchema.CurrentVersion, run.ContextJson)
            .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
            .Context;
        context.HostId.Value.ShouldBe(f.HostId);
        var variables = context.Variables.ForExecution();
        variables[new("playback-outcome")].Value.ShouldBe(new AutomationValue.Text(label));
        variables[new("cue-id")].Value.ShouldBe(new AutomationValue.Text(notice.CueId.ToString()));
        variables[new("cue-run-id")]
            .Value.ShouldBe(new AutomationValue.Text(notice.RunId.ToString()));
        variables[new("target-id")]
            .Value.ShouldBe(new AutomationValue.Text(notice.TargetId.ToString()));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualQueueOwner_TypedLifecycleDefersEffectsAfterCommit_AndHonoursDisable(
        bool disable
    )
    {
        await using var f = await RuntimeFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.Automations | HostFeatureFlags.PlayWithViewers
        );
        _ = await SaveExpandedChatAsync(
            f,
            "queue-lifecycle",
            """{"event":"QueueJoined"}""",
            "joined"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "queue-lifecycle",
            """{"event":"QueueCalled"}""",
            "called"
        );
        var x = ExpandedFixtureFor(f);
        var lifecycle = new AutomationFeatureLifecycle(
            () => x.Runtime,
            NullLogger<AutomationFeatureLifecycle>.Instance
        );
        var queue = new PlayQueueService(
            f.Database,
            TestEventBus.Create<AppEventKind>(),
            f.Clock,
            automations: lifecycle
        );
        _ = (
            await queue.ConfigureAsync(
                f.HostId,
                new(
                    "squad",
                    "Squad",
                    "Game",
                    4,
                    true,
                    PlayQueueSelectionMode.LeastRecentParticipation,
                    true,
                    120,
                    30,
                    15,
                    [],
                    []
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<PlayQueueResult<PlayQueueSummary>.Succeeded>();
        if (disable)
        {
            _ = await f.Features.DisableAsync(
                f.HostId,
                HostFeatureFlags.Automations,
                CancellationToken.None
            );
        }
        _ = (
            await queue.JoinAsync(
                f.HostId,
                "squad",
                new(new("viewer", "viewer-id", "Viewer"), 0, new Dictionary<string, string>()),
                CancellationToken.None
            )
        ).ShouldBeOfType<PlayQueueResult<PublicPlayQueueEntryView>.Succeeded>();
        long entryId;
        await using (var committed = await f.Database.CreateDbContextAsync())
        {
            entryId = (await committed.PlayQueueEntries.SingleAsync()).Id;
            (await committed.AutomationFlowRuns.CountAsync()).ShouldBe(disable ? 0 : 1);
        }
        _ = (
            await queue.StartReadyCheckAsync(f.HostId, entryId, CancellationToken.None)
        ).ShouldBeOfType<PlayQueueResult<ModeratorPlayQueueEntryView>.Succeeded>();
        f.Chat.Messages.ShouldBeEmpty();
        await using (var committed = await f.Database.CreateDbContextAsync())
        {
            (await committed.PlayQueueEntries.SingleAsync()).Status.ShouldBe(
                PlayQueueEntryStatus.AwaitingReady
            );
            (await committed.PlayQueueEvents.CountAsync()).ShouldBe(3);
            (await committed.AutomationFlowRuns.CountAsync()).ShouldBe(disable ? 0 : 2);
        }
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(disable ? [] : new[] { "joined", "called" }, ignoreOrder: true);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RealStreamEnd_ClearsSeenIdsEvenWhileDisabled_WithoutStaleOrUnknownCleanup(
        bool disable
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        var x = ExpandedFixtureFor(f);
        var now = f.Clock.GetUtcNow();
        x.Streams.Current = new HostStreamLivenessOutcome.Live("actual-stream", now.AddMinutes(-1));
        await x.Runtime.ChatReceivedAsync(
            new(
                "chat",
                now,
                "streamer-id",
                "streamer-id",
                "viewer",
                "viewer",
                "Viewer",
                "hello",
                []
            ),
            CancellationToken.None
        );
        f.Chat.Messages.ShouldBe(["first"]);
        if (disable)
        {
            _ = await f.Features.DisableAsync(
                f.HostId,
                HostFeatureFlags.Automations,
                CancellationToken.None
            );
        }
        x.Streams.Current = new HostStreamLivenessOutcome.Unavailable(
            HostStreamLivenessUnavailableReason.ProviderRequestFailed,
            new HttpRequestException()
        );
        await x.Runtime.TickAsync(CancellationToken.None);
        x.Connection.Current = new BotRuntimeStatus.Authorized();
        await x.Runtime.TickAsync(CancellationToken.None);
        var handler = ExpandedHandler(x.Runtime);
        var offline = ExpandedEnvelope(
            f,
            "stream.offline",
            "1",
            new
            {
                broadcaster_user_id = "streamer-id",
                broadcaster_user_login = "streamer",
                broadcaster_user_name = "Streamer",
            },
            "offline"
        );
        var stale = offline with
        {
            Metadata = offline.Metadata with
            {
                MessageId = "stale",
                MessageTimestamp = now.AddMinutes(-2),
            },
        };
        await handler.DispatchNotificationAsync(stale, "{}", CancellationToken.None);
        await handler.DispatchNotificationAsync(
            ExpandedEnvelope(
                f,
                "stream.offline",
                "1",
                new
                {
                    broadcaster_user_id = "other-host",
                    broadcaster_user_login = "other",
                    broadcaster_user_name = "Other",
                },
                "unowned"
            ),
            "{}",
            CancellationToken.None
        );
        await handler.DispatchNotificationAsync(
            ExpandedEnvelope(
                f,
                "stream.offline",
                "1",
                new
                {
                    broadcaster_user_id = "",
                    broadcaster_user_login = "streamer",
                    broadcaster_user_name = "Streamer",
                },
                "no-identity"
            ),
            "{}",
            CancellationToken.None
        );
        await using (var before = await f.Database.CreateDbContextAsync())
        {
            (await before.AutomationStreamObservations.SingleAsync()).StreamId.ShouldBe(
                "actual-stream"
            );
            (await before.AutomationSeenViewers.SingleAsync()).ViewerId.ShouldBe("viewer");
        }
        await handler.DispatchNotificationAsync(offline, "{}", CancellationToken.None);
        await handler.DispatchNotificationAsync(offline, "{}", CancellationToken.None);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationStreamObservations.CountAsync()).ShouldBe(0);
        (await db.AutomationSeenViewers.CountAsync()).ShouldBe(0);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
        (await db.AutomationFlows.CountAsync()).ShouldBe(1);
        f.Chat.Messages.ShouldBe(["first"]);
    }

    [Test]
    [Arguments("ban", "user_login", "123", "\"subject\"")]
    [Arguments("ban", "user_name", "[]", "\"Subject\"")]
    [Arguments("ban", "user_id", "{}", "\"subject-id\"")]
    [Arguments("ban", "reason", "123", "\"private reason\"")]
    [Arguments("shared_chat_ban", "user_login", "123", "\"subject\"")]
    [Arguments("shared_chat_ban", "user_name", "[]", "\"Subject\"")]
    [Arguments("shared_chat_ban", "reason", "{}", "\"private reason\"")]
    [Arguments("timeout", "expires_at", "\"not-a-time\"", "\"2026-08-03T12:00:30Z\"")]
    [Arguments("timeout", "expires_at", "123", "\"2026-08-03T12:00:30Z\"")]
    [Arguments("shared_chat_timeout", "expires_at", "\"not-a-time\"", "\"2026-08-03T12:00:30Z\"")]
    [Arguments("raid", "viewer_count", "\"4\"", "4")]
    [Arguments("raid", "viewer_count", "-1", "4")]
    [Arguments("raid", "viewer_count", "4.5", "4")]
    [Arguments("followers", "follow_duration_minutes", "\"4\"", "4")]
    [Arguments("slow", "wait_time_seconds", "[]", "30")]
    [Arguments("delete", "message_id", "123", "\"deleted-id\"")]
    [Arguments("shared_chat_delete", "message_body", "{}", "\"private body\"")]
    [Arguments("add_blocked_term", "terms", "{}", "[\"private term\"]")]
    [Arguments("remove_permitted_term", "terms", "[123]", "[\"private term\"]")]
    [Arguments("add_permitted_term", "from_automod", "\"false\"", "false")]
    [Arguments("remove_blocked_term", "list", "[]", "\"blocked\"")]
    [Arguments("add_blocked_term", "action", "true", "\"add\"")]
    [Arguments("approve_unban_request", "is_approved", "\"true\"", "true")]
    [Arguments("deny_unban_request", "moderator_message", "123", "\"private message\"")]
    [Arguments("warn", "chat_rules_cited", "[null]", "[\"private rule\"]")]
    [Arguments("warn", "reason", "[]", "\"private reason\"")]
    public async Task ModerateV2_SuppliedInvalidApplicableDetail_HasNoEffects_OptionalAndValidDetailStillRuns(
        string action,
        string field,
        string invalidJson,
        string validJson
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "moderation-action",
            JsonSerializer.Serialize(new { action }),
            "effect"
        );
        var handler = ExpandedHandler(ExpandedFixtureFor(f).Runtime);
        var key =
            action.Contains("_term", StringComparison.Ordinal) ? "automod_terms"
            : action.EndsWith("unban_request", StringComparison.Ordinal) ? "unban_request"
            : action;
        var payload = new Dictionary<string, JsonElement>
        {
            ["broadcaster_user_id"] = JsonSerializer.SerializeToElement("streamer-id"),
            ["source_broadcaster_user_id"] = JsonSerializer.SerializeToElement("remote-id"),
            ["source_broadcaster_user_login"] = JsonSerializer.SerializeToElement("remote"),
            ["moderator_user_id"] = JsonSerializer.SerializeToElement("moderator"),
            ["action"] = JsonSerializer.SerializeToElement(action),
        };
        using var invalid = JsonDocument.Parse(
            "{" + JsonSerializer.Serialize(field) + ":" + invalidJson + "}"
        );
        payload[key] = invalid.RootElement.Clone();
        await DispatchAsync("invalid");
        f.Chat.Messages.ShouldBeEmpty();
        await using (var rejected = await f.Database.CreateDbContextAsync())
        {
            (await rejected.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        }
        using var valid = JsonDocument.Parse(
            "{" + JsonSerializer.Serialize(field) + ":" + validJson + "}"
        );
        payload[key] = valid.RootElement.Clone();
        await DispatchAsync("valid");
        await DispatchAsync("valid");
        f.Chat.Messages.ShouldBe(["effect"]);
        payload[key] = JsonSerializer.SerializeToElement<object?>(null);
        await DispatchAsync("null-detail");
        _ = payload.Remove(key);
        await DispatchAsync("absent-detail");
        payload[key] = JsonSerializer.SerializeToElement(
            new Dictionary<string, object?> { [field] = null }
        );
        await DispatchAsync("null-field");
        f.Chat.Messages.ShouldBe(["effect", "effect", "effect", "effect"]);
        await using var db = await f.Database.CreateDbContextAsync();
        var contexts = await db.AutomationFlowRuns.Select(r => r.ContextJson).ToArrayAsync();
        contexts.Length.ShouldBe(4);
        foreach (var context in contexts)
        {
            context.ShouldContain("remote-id");
            context.ShouldNotContain("private");
        }

        Task DispatchAsync(string occurrence) =>
            handler.DispatchNotificationAsync(
                ExpandedEnvelope(f, "channel.moderate", "2", payload, occurrence),
                "{}",
                CancellationToken.None
            );
    }

    [Test]
    public async Task ModerateV2_IrrelevantExtraFields_DoNotNarrowApplicableMetadata()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(f, "moderation-action", """{"action":"any"}""", "effect");
        var handler = ExpandedHandler(ExpandedFixtureFor(f).Runtime);
        await handler.DispatchNotificationAsync(
            ExpandedEnvelope(
                f,
                "channel.moderate",
                "2",
                new
                {
                    broadcaster_user_id = "streamer-id",
                    source_broadcaster_user_id = "streamer-id",
                    moderator_user_id = "moderator",
                    action = "ban",
                    ban = new
                    {
                        user_login = "subject",
                        user_name = "Subject",
                        expires_at = "not-a-time",
                        viewer_count = new[] { 1 },
                        wait_time_seconds = false,
                        terms = 123,
                    },
                    timeout = new[] { 1 },
                },
                "ban"
            ),
            "{}",
            CancellationToken.None
        );
        await handler.DispatchNotificationAsync(
            ExpandedEnvelope(
                f,
                "channel.moderate",
                "2",
                new
                {
                    broadcaster_user_id = "streamer-id",
                    source_broadcaster_user_id = "streamer-id",
                    moderator_user_id = "moderator",
                    action = "clear",
                    clear = new[] { 1 },
                },
                "clear"
            ),
            "{}",
            CancellationToken.None
        );
        f.Chat.Messages.ShouldBe(["effect", "effect"]);
    }
}
