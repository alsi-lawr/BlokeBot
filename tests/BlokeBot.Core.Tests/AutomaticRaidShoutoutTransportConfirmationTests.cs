using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidShoutoutTransportConfirmationTests
    : PublicChatOutboxIntegrationTestBase
{
    private static readonly DateTimeOffset _now = new(2026, 8, 25, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task BlockedTransport_RemainsQueuedUntilTheSendCompletes()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database);
        var clock = new ManualTestTimeProvider(_now);
        var events = TestEventBus.Create<AppEventKind>();
        var deliveredNotification = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var subscription = events.Subscribe(
            AppEventKind.RaidCollaborationChanged,
            ObserverIdentity.Named("Test.AutomaticRaidTransportConfirmed"),
            (_, _) =>
            {
                deliveredNotification.SetResult();
                return ValueTask.CompletedTask;
            }
        );
        var alerts = new DurableAlertService(database, clock, events);
        var authority = new AutomaticRaidShoutoutOutcomeAuthority(events);
        var outbox = new EfPublicChatOutbox(
            database,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy,
            alerts,
            authority
        );
        var sendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseSend = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var transport = new ScriptedPublicChatTransport(
            (message, cancellationToken) =>
                ValueTask.FromResult<PublicChatPreparationOutcome>(
                    new PublicChatPreparationOutcome.Ready { Send = Prepared(message) }
                ),
            async (_, cancellationToken) =>
            {
                sendStarted.SetResult();
                await releaseSend.Task.WaitAsync(cancellationToken);
                return new PublicChatTransportSendResult.Sent("twitch-message");
            }
        );
        var queue = CreateQueue(
            outbox,
            transport,
            clock,
            new BotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 0,
            }
        );
        var delivery = new AutomaticRaidShoutoutDelivery(
            new UnusedNativeSender(),
            new UnavailableChannelInformation(),
            new PublicChatMessageSender(queue),
            new UnusedAnnouncementSender(),
            database,
            alerts
        );
        var runner = new AutomaticRaidShoutoutRunner(database, delivery, authority, clock);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);
        try
        {
            var immediate = await runner.RunAsync(
                host,
                Configuration(),
                Raid(),
                CancellationToken.None
            );
            await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            immediate.ShouldBe(AutomaticRaidShoutoutResultCode.Queued);
            await AssertOutcomeAsync(
                database,
                AutomaticRaidShoutoutOutcomeStatus.Queued,
                AutomaticRaidShoutoutResultCode.Queued
            );
            deliveredNotification.Task.IsCompleted.ShouldBeFalse();

            releaseSend.SetResult();
            await deliveredNotification.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForOutcomeAsync(database, AutomaticRaidShoutoutOutcomeStatus.Delivered);
            await AssertOutcomeAsync(
                database,
                AutomaticRaidShoutoutOutcomeStatus.Delivered,
                AutomaticRaidShoutoutResultCode.Delivered
            );
        }
        finally
        {
            _ = releaseSend.TrySetResult();
            await StopAsync(stopping, worker);
        }
    }

    [Test]
    public async Task TerminalCallback_BetweenRunnerAndTrackedRaidSave_CannotDowngradeCanonicalHistory()
    {
        var pause = new PauseRaidHistorySaveInterceptor();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(pause);
        var host = await SeedHostAsync(database);
        await SeedRaidConfigurationAsync(database, host.Id);
        var clock = new ManualTestTimeProvider(_now);
        var events = TestEventBus.Create<AppEventKind>();
        var alerts = new DurableAlertService(database, clock, events);
        var authority = new AutomaticRaidShoutoutOutcomeAuthority(events);
        var outbox = new EfPublicChatOutbox(
            database,
            StandardRetryPolicy,
            StandardLifetimePolicy,
            StandardRetentionPolicy,
            alerts,
            authority
        );
        var queue = CreateQueue(
            outbox,
            new RecordingPublicChatTransport(),
            clock,
            new BotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 0,
            }
        );
        var delivery = new AutomaticRaidShoutoutDelivery(
            new UnusedNativeSender(),
            new UnavailableChannelInformation(),
            new PublicChatMessageSender(queue),
            new UnusedAnnouncementSender(),
            database,
            alerts
        );
        var service = new RaidCollaborationService(
            database,
            new AvailableRaidProvider(),
            new UnusedWelcomeSender(),
            new UnusedShoutoutOperations(),
            new AutomaticRaidShoutoutRunner(database, delivery, authority, clock),
            [],
            events,
            clock
        );

        var recording = service.IncomingRaidReceivedAsync(Raid(), CancellationToken.None);
        await pause.WaitUntilPausedAsync().WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await AssertOutcomeAndHistoryAsync(
                database,
                AutomaticRaidShoutoutOutcomeStatus.Queued,
                AutomaticRaidShoutoutResultCode.Queued,
                RaidShoutoutOutcome.Queued
            );
            var claimed = await ClaimAsync(outbox, _now, TimeSpan.Zero);
            _ = (
                await outbox.BeginSendAsync(
                    claimed,
                    _now,
                    _now.AddMinutes(5),
                    CancellationToken.None
                )
            ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
            _ = (
                await outbox.RecordDeliveryOutcomeAsync(
                    claimed,
                    new PublicChatDeliveryOutcome.Rejection
                    {
                        Reason = new PublicChatRejectionReason.ProviderCode(
                            new PublicChatProviderRejectionCode("msg_rejected")
                        ),
                    },
                    _now.AddSeconds(1),
                    CancellationToken.None
                )
            ).ShouldBeOfType<PublicChatClaimUpdate.Applied>();
            await AssertOutcomeAndHistoryAsync(
                database,
                AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
                AutomaticRaidShoutoutResultCode.Rejected,
                RaidShoutoutOutcome.Rejected
            );
        }
        finally
        {
            pause.Release();
        }

        await recording;
        await AssertOutcomeAndHistoryAsync(
            database,
            AutomaticRaidShoutoutOutcomeStatus.NotDelivered,
            AutomaticRaidShoutoutResultCode.Rejected,
            RaidShoutoutOutcome.Rejected
        );
    }

    private static async Task<BotHost> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "host-id",
            Login = "host",
            DisplayName = "Host",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host;
    }

    private static async Task SeedRaidConfigurationAsync(
        SqliteBlokeBotDbFactory database,
        int hostId
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.RaidCollaborationSettings.Add(
            new RaidCollaborationSettings
            {
                HostId = hostId,
                WelcomeEnabled = false,
                DeduplicationWindowMinutes = 60,
                RelationshipCooldownHours = 24,
                UpdatedAtUtc = _now.UtcDateTime,
            }
        );
        _ = db.AutomaticRaidShoutoutSettings.Add(
            new AutomaticRaidShoutoutSettings
            {
                HostId = hostId,
                Enabled = true,
                MinimumViewerCount = 1,
                Mechanism = AutomaticRaidShoutoutMechanism.Chat,
                ChatPresentation = AutomaticRaidChatPresentation.Regular,
                MessageTemplate = "Welcome {display_name}",
                UpdatedAtUtc = _now.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static AutomaticRaidShoutoutConfiguration Configuration() =>
        new(
            true,
            false,
            1,
            AutomaticRaidShoutoutMechanism.Chat,
            AutomaticRaidChatPresentation.Regular,
            "Welcome {display_name}",
            null,
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Primary
        );

    private static EventSubIncomingRaidEvent Raid() =>
        new("raid-message", _now, "raider-id", "raider", "Raider", "host-id", "host", "Host", 10);

    private static async Task WaitForOutcomeAsync(
        SqliteBlokeBotDbFactory database,
        AutomaticRaidShoutoutOutcomeStatus expected
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            await using var db = await database.CreateDbContextAsync();
            if (
                await db.AutomaticRaidShoutoutOutcomes.AnyAsync(
                    outcome => outcome.Status == expected,
                    timeout.Token
                )
            )
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task AssertOutcomeAsync(
        SqliteBlokeBotDbFactory database,
        AutomaticRaidShoutoutOutcomeStatus status,
        AutomaticRaidShoutoutResultCode resultCode
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var outcome = await db.AutomaticRaidShoutoutOutcomes.SingleAsync();
        outcome.Status.ShouldBe(status);
        outcome.ResultCode.ShouldBe(resultCode);
    }

    private static async Task AssertOutcomeAndHistoryAsync(
        SqliteBlokeBotDbFactory database,
        AutomaticRaidShoutoutOutcomeStatus status,
        AutomaticRaidShoutoutResultCode resultCode,
        RaidShoutoutOutcome historyOutcome
    )
    {
        await AssertOutcomeAsync(database, status, resultCode);
        await using var db = await database.CreateDbContextAsync();
        (await db.RaidCollaborationHistory.SingleAsync()).ShoutoutOutcome.ShouldBe(historyOutcome);
    }

    private sealed class PauseRaidHistorySaveInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _paused = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _claimed;

        internal Task WaitUntilPausedAsync() => _paused.Task;

        internal void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                eventData
                    .Context?.ChangeTracker.Entries<RaidCollaborationHistoryEntry>()
                    .Any(entry =>
                        entry.State == EntityState.Modified
                        && entry.Entity.ProviderMessageId == "raid-message"
                    ) == true
                && Interlocked.Exchange(ref _claimed, 1) == 0
            )
            {
                _ = _paused.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class AvailableRaidProvider : IRaidCollaborationProvider
    {
        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
            int hostId,
            string login,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Available(
                    new(
                        "raider-id",
                        "raider",
                        "Raider",
                        "stream-id",
                        "Raid game",
                        "en",
                        "Raid title",
                        10,
                        null
                    )
                )
            );

        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelByIdAsync(
            int hostId,
            string twitchUserId,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Incoming raid recording loads by login.");

        public Task<FollowedLiveChannelsOutcome> LoadFollowedLiveChannelsAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "Incoming raid recording does not load followed channels."
            );

        public Task<bool> HasFollowedLiveAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "Incoming raid recording does not inspect followed authorization."
            );

        public Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
            int hostId,
            string targetTwitchUserId,
            string targetLogin,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Incoming raid recording does not start raids.");

        public Task<bool> HasRaidManagementAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "Incoming raid recording does not inspect raid management."
            );
    }

    private sealed class UnusedWelcomeSender : IRaidWelcomeSender
    {
        public Task<bool> SendAsync(
            int hostId,
            string hostLogin,
            string providerMessageId,
            string message,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Welcome delivery is disabled.");
    }

    private sealed class UnusedShoutoutOperations : IShoutoutDashboardOperations
    {
        public Task<ShoutoutDashboardState> LoadAsync(
            int hostId,
            string? targetLogin,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException("Incoming raid recording does not load shoutouts.");

        public Task<ShoutoutOperationOutcome> SendAsync(
            int hostId,
            string targetLogin,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "Automatic raid delivery does not use dashboard shoutouts."
            );
    }

    private sealed class UnusedNativeSender : IAutomaticRaidNativeShoutoutSender
    {
        public Task<AutomaticRaidShoutoutDeliveryResult> SendAsync(
            int hostId,
            string targetLogin,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Chat delivery must not use native shoutouts.");
    }

    private sealed class UnavailableChannelInformation : IAutomaticRaidChannelInformationProvider
    {
        public Task<AutomaticRaidChannelInformationResult> GetAsync(
            string raiderTwitchUserId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<AutomaticRaidChannelInformationResult>(
                new AutomaticRaidChannelInformationResult.Unavailable()
            );
    }

    private sealed class UnusedAnnouncementSender : IAutomaticRaidAnnouncementSender
    {
        public Task<AutomaticRaidAnnouncementSendResult> SendAsync(
            string channelLogin,
            string message,
            BlokeBot.Persistence.Models.TwitchAnnouncementColor color,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Regular chat must not use announcements.");
    }
}
