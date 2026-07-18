using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class FollowerOnlyChatAlertIntegrationTests
{
    [Test]
    public async Task ExactFollowersOnlyTerminalRejection_PersistsOneChannelSetupAlert()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var clock = new ManualTestTimeProvider(Utc(12, 0, 0));
        var alerts = new DurableAlertService(dbFactory, clock, TestEventBus.Create<AppEventKind>());
        var observer = new DurableFollowerOnlyChatAlertObserver(dbFactory, alerts);
        var outbox = new CompletionObservingPublicChatOutbox(
            new EfPublicChatOutbox(
                dbFactory,
                StandardRetryPolicy,
                StandardLifetimePolicy,
                StandardRetentionPolicy
            )
        );
        var transport = new ScriptedPublicChatTransport(
            static (message, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<PublicChatPreparationOutcome>(
                    new PublicChatPreparationOutcome.Ready { Send = Prepared(message) }
                );
            },
            static (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult<PublicChatTransportSendResult>(
                    new PublicChatTransportSendResult.Rejected
                    {
                        Reason = new PublicChatRejectionReason.ProviderCode(
                            new PublicChatProviderRejectionCode("followers_only")
                        ),
                    }
                );
            }
        );
        var queue = CreateQueue(outbox, transport, clock, rejectionObservers: [observer]);
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await queue.EnqueueAsync(Command("streamer", "first"), CancellationToken.None);
        _ = await outbox.ReadDeliveryAsync();
        _ = await queue.EnqueueAsync(Command("streamer", "second"), CancellationToken.None);
        _ = await outbox.ReadDeliveryAsync();
        await StopAsync(stopping, worker);

        var state = await alerts.LoadStateAsync(hostId, CancellationToken.None);
        var alert = state.Active.ShouldHaveSingleItem();
        alert.Source.ShouldBe("twitch-follower-only-chat");
        alert.SourceKey.ShouldBe("followers_only");
        alert.LinkPath.ShouldBe("/host");
    }

    [Test]
    public async Task OtherProviderCodes_ObservingTerminalRejection_CreateNoFollowerOnlyAlert()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var alerts = new DurableAlertService(
            dbFactory,
            new ManualTestTimeProvider(Utc(12, 0, 0)),
            TestEventBus.Create<AppEventKind>()
        );
        var observer = new DurableFollowerOnlyChatAlertObserver(dbFactory, alerts);

        await observer.TerminalRejectionAsync(
            new PublicChatTerminalRejection("streamer", "Followers_Only"),
            CancellationToken.None
        );
        await observer.TerminalRejectionAsync(
            new PublicChatTerminalRejection("streamer", "subscriber_only"),
            CancellationToken.None
        );

        (await alerts.LoadStateAsync(hostId, CancellationToken.None)).Active.ShouldBeEmpty();
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
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
