using System.Threading.Channels;
using BlokeBot.Eventing;
using BlokeBot.Features.Alerts;
using BlokeBot.Features.PublicChat;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;
using static BlokeBot.Integration.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Integration.Tests;

public sealed class PublicChatQueueAlertIntegrationTests
{
    [Test]
    public async Task PublicChatQueueBackup_DetectingIncident_PersistsAlertAndPublishesApplicationNotification()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var events = TestEventBus.Create<AppEventKind>();
        var notifications = Channel.CreateUnbounded<bool>();
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.AlertCreated"),
            (_, _) =>
            {
                if (!notifications.Writer.TryWrite(true))
                {
                    throw new InvalidOperationException(
                        "The alert notification could not be observed."
                    );
                }

                return ValueTask.CompletedTask;
            }
        );
        var alertService = new DurableAlertService(dbFactory, clock, events);
        var durableObserver = new DurablePublicChatQueueAlertObserver(
            dbFactory,
            alertService,
            NullLogger<DurablePublicChatQueueAlertObserver>.Instance
        );
        var outbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(dbFactory, StandardRetryPolicy, StandardLifetimePolicy, StandardRetentionPolicy)
        );
        var transport = new RecordingPublicChatTransport();
        var queue = CreateQueue(
            outbox,
            transport,
            clock,
            new TwitchBotOptions
            {
                ChatMessageSendIntervalSeconds = 10,
                DuplicateChatMessageCooldownSeconds = 0,
                PublicChatQueueAlerts = new PublicChatQueueAlertOptions
                {
                    StuckAfterSeconds = 5,
                },
            },
            [durableObserver]
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await queue.EnqueueAsync(
            Command("streamer", "first"),
            CancellationToken.None
        );
        _ = await transport.ReadAsync();
        _ = await outbox.ReadDeliveryAsync();
        _ = await queue.EnqueueAsync(
            Command("streamer", "second"),
            CancellationToken.None
        );
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        _ = await notifications.Reader.ReadAsync();

        var state = await alertService.LoadStateAsync(hostId, CancellationToken.None);
        var alert = state.Active.ShouldHaveSingleItem();
        alert.Source.ShouldBe("twitch-outbound-queue");
        alert.LinkPath.ShouldBe("/alerts");

        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        (await transport.ReadAsync()).Message.ShouldBe("second");
        _ = await outbox.ReadDeliveryAsync();
        await StopAsync(stopping, worker);
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            TwitchUserId = "streamer-id",
            CreatedAtUtc = Utc(12, 0, 0).UtcDateTime,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
