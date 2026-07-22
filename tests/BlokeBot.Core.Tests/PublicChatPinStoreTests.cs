using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Persistence.Models;
using BlokeBot.Testing;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PublicChatPinStoreTests
{
    [Test]
    public async Task ReplacementPin_SameHostChannel_ReplacesRecordedOwnership()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAndOperationAsync(
            dbFactory,
            3,
            PublicChatPinOperationStatus.Attempting,
            GuessRoundStatus.Open
        );
        var store = Store(dbFactory);
        var original = (await store.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();
        await store.CompleteAsync(
            original,
            new PublicChatPinExecutionOutcome.Pinned("bot-user-id"),
            CancellationToken.None
        );
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.PublicChatPinOperations.Add(
                new PublicChatPinOperation
                {
                    Kind = PublicChatPinOperationKind.Pin,
                    Status = PublicChatPinOperationStatus.Attempting,
                    HostId = 3,
                    Channel = "streamer3",
                    Feature = "guessing",
                    ReplyKey = "round_started",
                    OwnerId = original.OwnerId,
                    TwitchMessageId = "replacement-message",
                    CreatedAtUtc = DateTime.UtcNow,
                    AttemptStartedAtUtc = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        var replacement = (await store.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();
        await store.CompleteAsync(
            replacement,
            new PublicChatPinExecutionOutcome.Pinned("bot-user-id"),
            CancellationToken.None
        );

        await using var verify = await dbFactory.CreateDbContextAsync();
        var active = await verify.ActivePublicChatPins.SingleAsync();
        active.HostId.ShouldBe(3);
        active.TwitchMessageId.ShouldBe("replacement-message");
    }

    [Test]
    public async Task ExactUnpin_ForOneHost_PreservesOtherHostOwnership()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAndOperationAsync(
            dbFactory,
            4,
            PublicChatPinOperationStatus.Attempting,
            GuessRoundStatus.Open
        );
        await SeedHostAndOperationAsync(
            dbFactory,
            5,
            PublicChatPinOperationStatus.Attempting,
            GuessRoundStatus.Open
        );
        var store = Store(dbFactory);
        for (var index = 0; index < 2; index++)
        {
            var pin = (await store.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();
            await store.CompleteAsync(
                pin,
                new PublicChatPinExecutionOutcome.Pinned("bot-user-id"),
                CancellationToken.None
            );
        }

        long ownerId;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var owned = await db.ActivePublicChatPins.SingleAsync(pin => pin.HostId == 5);
            ownerId = owned.OwnerId;
            db.PublicChatPinOperations.Add(
                new PublicChatPinOperation
                {
                    Kind = PublicChatPinOperationKind.Unpin,
                    Status = PublicChatPinOperationStatus.Attempting,
                    HostId = 5,
                    Channel = owned.Channel,
                    Feature = owned.Feature,
                    ReplyKey = owned.ReplyKey,
                    OwnerId = owned.OwnerId,
                    TwitchMessageId = owned.TwitchMessageId,
                    PinnerTwitchUserId = owned.PinnerTwitchUserId,
                    CreatedAtUtc = DateTime.UtcNow,
                    AttemptStartedAtUtc = DateTime.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }
        var unpin = (await store.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();
        unpin.HostId.ShouldBe(5);
        unpin.OwnerId.ShouldBe(ownerId);
        await store.CompleteAsync(
            unpin,
            new PublicChatPinExecutionOutcome.Unpinned(),
            CancellationToken.None
        );

        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.ActivePublicChatPins.SingleAsync()).HostId.ShouldBe(4);
    }

    [Test]
    public async Task AttemptingPin_AfterRestart_IsClaimedForReadOnlyReconciliation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAndOperationAsync(
            dbFactory,
            1,
            PublicChatPinOperationStatus.Attempting,
            GuessRoundStatus.Open
        );
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var store = new EfPublicChatPinStore(
            dbFactory,
            new ManualTestTimeProvider(now),
            TestEventBus.Create<AppEventKind>()
        );

        var item = await store.TryClaimAsync(CancellationToken.None);

        item.ShouldNotBeNull();
        item.ReconcileOnly.ShouldBeTrue();
        item.TwitchMessageId.ShouldBe("message-1");
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.PublicChatPinOperations.SingleAsync()).Status.ShouldBe(
            PublicChatPinOperationStatus.Attempting
        );
    }

    [Test]
    public async Task PinAcceptedAfterRoundStopped_QueuesCompensatingExactUnpin()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAndOperationAsync(
            dbFactory,
            2,
            PublicChatPinOperationStatus.Attempting,
            GuessRoundStatus.Closed
        );
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var store = new EfPublicChatPinStore(
            dbFactory,
            new ManualTestTimeProvider(now),
            TestEventBus.Create<AppEventKind>()
        );
        var item = (await store.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();

        await store.CompleteAsync(
            item,
            new PublicChatPinExecutionOutcome.Pinned("bot-user-id"),
            CancellationToken.None
        );

        await using var verify = await dbFactory.CreateDbContextAsync();
        var active = await verify.ActivePublicChatPins.SingleAsync();
        active.HostId.ShouldBe(2);
        active.TwitchMessageId.ShouldBe("message-2");
        active.PinnerTwitchUserId.ShouldBe("bot-user-id");
        var reset = await verify.PublicChatPinOperations.SingleAsync(operation =>
            operation.Kind == PublicChatPinOperationKind.Unpin
        );
        reset.Status.ShouldBe(PublicChatPinOperationStatus.Ready);
        reset.OwnerId.ShouldBe(item.OwnerId);
        reset.TwitchMessageId.ShouldBe(item.TwitchMessageId);
    }

    private static async Task SeedHostAndOperationAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        PublicChatPinOperationStatus operationStatus,
        GuessRoundStatus roundStatus
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Hosts.Add(
            new BotHost
            {
                Id = hostId,
                Login = $"streamer{hostId}",
                DisplayName = $"Streamer {hostId}",
                CreatedAtUtc = DateTime.UtcNow,
            }
        );
        var profile = new GuessRoundProfile
        {
            HostId = hostId,
            Name = "default",
            Slug = "default",
            IsDefault = true,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        var round = new GuessRound
        {
            HostId = hostId,
            GuessRoundProfileId = profile.Id,
            Status = roundStatus,
            StartedAtUtc = DateTime.UtcNow,
            ClosedAtUtc = roundStatus == GuessRoundStatus.Closed ? DateTime.UtcNow : null,
        };
        db.Rounds.Add(round);
        await db.SaveChangesAsync();
        db.PublicChatPinOperations.Add(
            new PublicChatPinOperation
            {
                Kind = PublicChatPinOperationKind.Pin,
                Status = operationStatus,
                HostId = hostId,
                Channel = $"streamer{hostId}",
                Feature = "guessing",
                ReplyKey = "round_started",
                OwnerId = round.Id,
                TwitchMessageId = $"message-{hostId}",
                DurationSeconds = 300,
                UnpinOnOwnerCompletion = true,
                CreatedAtUtc = DateTime.UtcNow,
                AttemptStartedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    private static EfPublicChatPinStore Store(SqliteBlokeBotDbFactory dbFactory)
    {
        return new(
            dbFactory,
            new ManualTestTimeProvider(new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero)),
            TestEventBus.Create<AppEventKind>()
        );
    }
}
