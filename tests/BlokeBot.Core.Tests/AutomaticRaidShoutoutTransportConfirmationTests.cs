using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class AutomaticRaidShoutoutTransportConfirmationTests
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
