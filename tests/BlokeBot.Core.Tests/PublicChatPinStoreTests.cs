using BlokeBot.Core.Features.PublicChat;
using BlokeBot.Persistence.Models;
using BlokeBot.Testing;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PublicChatPinStoreTests
{
    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 1)]
    public async Task PinAcceptedAfterRoundStopped_QueuesOnlyConfiguredCompensation(
        bool unpinOnCompletion,
        int expectedResetCount
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedPinOperationAsync(
            dbFactory,
            PublicChatPinOperationStatus.Attempting,
            GuessRoundStatus.Closed,
            unpinOnCompletion
        );
        var store = Store(dbFactory);
        var item = (await store.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();
        var accepted = new PublicChatPinExecutionOutcome.Pinned("recorded-pinner");

        await store.CompleteAsync(item, accepted, CancellationToken.None);
        await store.CompleteAsync(item, accepted, CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        (
            await verify.PublicChatPinOperations.CountAsync(operation =>
                operation.Kind == PublicChatPinOperationKind.Unpin
            )
        ).ShouldBe(expectedResetCount);
        var active = await verify.ActivePublicChatPins.SingleAsync();
        active.UnpinOnOwnerCompletion.ShouldBe(unpinOnCompletion);
        active.PinnerTwitchUserId.ShouldBe("recorded-pinner");
    }

    [Test]
    [Arguments(PublicChatPinOperationStatus.Ready, false)]
    [Arguments(PublicChatPinOperationStatus.Attempting, true)]
    public async Task Restart_ReadyRemainsAttemptable_WhileAttemptingIsReconcileOnly(
        PublicChatPinOperationStatus status,
        bool expectedReconcileOnly
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedPinOperationAsync(dbFactory, status, GuessRoundStatus.Open, true);
        var store = Store(dbFactory);

        var item = (await store.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();

        item.ReconcileOnly.ShouldBe(expectedReconcileOnly);
        if (status == PublicChatPinOperationStatus.Ready)
        {
            var afterClaimRestart = (
                await Store(dbFactory).TryClaimAsync(CancellationToken.None)
            ).ShouldNotBeNull();
            afterClaimRestart.ReconcileOnly.ShouldBeFalse();
            (await store.BeginAttemptAsync(item, CancellationToken.None)).ShouldBeTrue();
            (await store.BeginAttemptAsync(item, CancellationToken.None)).ShouldBeFalse();
        }
        else
        {
            (await store.BeginAttemptAsync(item, CancellationToken.None)).ShouldBeFalse();
        }

        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.PublicChatPinOperations.SingleAsync()).Status.ShouldBe(
            PublicChatPinOperationStatus.Attempting
        );
    }

    [Test]
    [Arguments("exact", typeof(PublicChatPinExecutionOutcome.Pinned))]
    [Arguments("different-pinner", typeof(PublicChatPinExecutionOutcome.Terminal))]
    public void AttemptingPin_ReconciliationRequiresExactMessageAndAttemptedPinner(
        string scenario,
        Type expectedType
    )
    {
        var item = WorkItem(isUnpin: false, recordedPinner: null);
        var outcome = PublicChatPinProviderDecision.ClassifyPinRead(
            item,
            CurrentPin(scenario),
            "recorded-pinner",
            "ambiguous"
        );

        outcome.GetType().ShouldBe(expectedType);
        if (outcome is PublicChatPinExecutionOutcome.Pinned pinned)
        {
            pinned.PinnerTwitchUserId.ShouldBe("recorded-pinner");
        }
        else
        {
            outcome
                .ShouldBeOfType<PublicChatPinExecutionOutcome.Terminal>()
                .Reason.ShouldBe("ambiguous");
        }
    }

    [Test]
    [Arguments("different-message", false, "replaced-or-not-recorded-owner")]
    [Arguments("different-pinner", true, "replaced-or-not-recorded-owner")]
    [Arguments("unavailable", true, "read-unavailable")]
    [Arguments("exact-unpinned", false, "unpinned")]
    public async Task UnpinCompletion_IsHostScoped_WhileRecoveryFailuresRetainOwnership(
        string scenario,
        bool ownershipRetained,
        string expectedReason
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedUnpinOperationAndOwnershipAsync(dbFactory);
        var store = Store(dbFactory);
        var item = (await store.TryClaimAsync(CancellationToken.None)).ShouldNotBeNull();
        item.RecordedPinnerTwitchUserId.ShouldBe("recorded-pinner");
        if (scenario == "different-pinner")
        {
            await using var replace = await dbFactory.CreateDbContextAsync();
            var newerOwnership = await replace.ActivePublicChatPins.SingleAsync(pin =>
                pin.HostId == item.HostId
            );
            newerOwnership.PinnerTwitchUserId = "other-pinner";
            await replace.SaveChangesAsync();
        }
        AssertUnpinTerminal(item, "exact", "unpin-ambiguous-after-restart");
        AssertUnpinTerminal(item, "permission", "permission-denied");
        AssertUnpinTerminal(item, "rate", "rate-limited");
        AssertUnpinTerminal(
            item with
            {
                RecordedPinnerTwitchUserId = null,
            },
            "exact",
            "missing-recorded-pinner"
        );
        PublicChatPinProviderDecision
            .ClassifyUnpinRead(
                item,
                CurrentPin("absent"),
                static () =>
                    new PublicChatPinExecutionOutcome.Terminal("unpin-ambiguous-after-restart")
            )
            .ShouldBeOfType<PublicChatPinExecutionOutcome.NoOp>();
        PublicChatPinExecutionOutcome outcome =
            scenario == "exact-unpinned"
                ? new PublicChatPinExecutionOutcome.Unpinned()
                : PublicChatPinProviderDecision
                    .ClassifyUnpinRead(
                        item,
                        CurrentPin(scenario),
                        static () =>
                            new PublicChatPinExecutionOutcome.Terminal(
                                "unpin-ambiguous-after-restart"
                            )
                    )
                    .ShouldNotBeNull();

        await store.CompleteAsync(item, outcome, CancellationToken.None);

        (
            outcome switch
            {
                PublicChatPinExecutionOutcome.NoOp noOp => noOp.Reason,
                PublicChatPinExecutionOutcome.Terminal terminal => terminal.Reason,
                PublicChatPinExecutionOutcome.Unpinned => "unpinned",
                _ => throw new InvalidOperationException("Unexpected reconciliation outcome."),
            }
        ).ShouldBe(expectedReason);
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.ActivePublicChatPins.AnyAsync(pin => pin.HostId == item.HostId)).ShouldBe(
            ownershipRetained
        );
        (
            await verify.ActivePublicChatPins.AnyAsync(pin => pin.HostId != item.HostId)
        ).ShouldBeTrue();
        if (outcome is PublicChatPinExecutionOutcome.Terminal)
        {
            (await verify.DurableAlerts.AnyAsync()).ShouldBeTrue();
        }
    }

    private static void AssertUnpinTerminal(
        PublicChatPinWorkItem item,
        string scenario,
        string expectedReason
    )
    {
        PublicChatPinProviderDecision
            .ClassifyUnpinRead(
                item,
                CurrentPin(scenario),
                static () =>
                    new PublicChatPinExecutionOutcome.Terminal("unpin-ambiguous-after-restart")
            )
            .ShouldBeOfType<PublicChatPinExecutionOutcome.Terminal>()
            .Reason.ShouldBe(expectedReason);
    }

    private static ChatPinnedMessageResult CurrentPin(string scenario)
    {
        return scenario switch
        {
            "exact" => new ChatPinnedMessageResult.Found("message-id", "recorded-pinner"),
            "different-message" => new ChatPinnedMessageResult.Found(
                "replacement",
                "recorded-pinner"
            ),
            "different-pinner" => new ChatPinnedMessageResult.Found("message-id", "other-pinner"),
            "absent" => new ChatPinnedMessageResult.Absent(),
            "permission" => new ChatPinnedMessageResult.PermissionDenied(),
            "rate" => new ChatPinnedMessageResult.RateLimited(),
            "unavailable" => new ChatPinnedMessageResult.Unavailable(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static async Task SeedPinOperationAsync(
        SqliteBlokeBotDbFactory dbFactory,
        PublicChatPinOperationStatus status,
        GuessRoundStatus roundStatus,
        bool unpinOnCompletion
    )
    {
        var (hostId, roundId) = await SeedHostAndRoundAsync(dbFactory, roundStatus);
        await using var db = await dbFactory.CreateDbContextAsync();
        db.PublicChatPinOperations.Add(
            new PublicChatPinOperation
            {
                Kind = PublicChatPinOperationKind.Pin,
                Status = status,
                HostId = hostId,
                Channel = "streamer",
                Feature = "guessing",
                ReplyKey = "round_started",
                OwnerId = roundId,
                TwitchMessageId = "message-id",
                DurationSeconds = 300,
                UnpinOnOwnerCompletion = unpinOnCompletion,
                CreatedAtUtc = DateTime.UtcNow,
                AttemptStartedAtUtc =
                    status == PublicChatPinOperationStatus.Attempting ? DateTime.UtcNow : null,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedUnpinOperationAndOwnershipAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        var (hostId, roundId) = await SeedHostAndRoundAsync(dbFactory, GuessRoundStatus.Closed);
        await using var db = await dbFactory.CreateDbContextAsync();
        var otherHost = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "other-streamer",
            DisplayName = "Other Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(otherHost);
        await db.SaveChangesAsync();
        db.ActivePublicChatPins.Add(
            new ActivePublicChatPin
            {
                HostId = hostId,
                Channel = "streamer",
                TwitchMessageId = "message-id",
                PinnerTwitchUserId = "recorded-pinner",
                Feature = "guessing",
                ReplyKey = "round_started",
                OwnerId = roundId,
                UnpinOnOwnerCompletion = true,
                PinnedAtUtc = DateTime.UtcNow,
            }
        );
        db.ActivePublicChatPins.Add(
            new ActivePublicChatPin
            {
                HostId = otherHost.Id,
                Channel = "streamer",
                TwitchMessageId = "message-id",
                PinnerTwitchUserId = "recorded-pinner",
                Feature = "guessing",
                ReplyKey = "round_started",
                OwnerId = roundId,
                UnpinOnOwnerCompletion = true,
                PinnedAtUtc = DateTime.UtcNow,
            }
        );
        db.PublicChatPinOperations.Add(
            new PublicChatPinOperation
            {
                Kind = PublicChatPinOperationKind.Unpin,
                Status = PublicChatPinOperationStatus.Attempting,
                HostId = hostId,
                Channel = "streamer",
                Feature = "guessing",
                ReplyKey = "round_started",
                OwnerId = roundId,
                TwitchMessageId = "message-id",
                PinnerTwitchUserId = "recorded-pinner",
                CreatedAtUtc = DateTime.UtcNow,
                AttemptStartedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<(int HostId, int RoundId)> SeedHostAndRoundAsync(
        SqliteBlokeBotDbFactory dbFactory,
        GuessRoundStatus roundStatus
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var profile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "default",
            Slug = "default",
            IsDefault = true,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        var round = new GuessRound
        {
            HostId = host.Id,
            GuessRoundProfileId = profile.Id,
            Status = roundStatus,
            StartedAtUtc = DateTime.UtcNow,
            ClosedAtUtc = roundStatus == GuessRoundStatus.Closed ? DateTime.UtcNow : null,
        };
        db.Rounds.Add(round);
        await db.SaveChangesAsync();
        return (host.Id, round.Id);
    }

    private static PublicChatPinWorkItem WorkItem(bool isUnpin, string? recordedPinner)
    {
        return new(
            1,
            true,
            isUnpin,
            1,
            "streamer",
            "guessing",
            "round_started",
            1,
            "message-id",
            recordedPinner,
            300,
            true
        );
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
