using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class BoundedDiscriminatorBehaviorTests
{
    [Test]
    public void ReplyDeliveryTarget_Mapping_CoversChatAndWhisper()
    {
        var delivery = ReplyDeliveryMap.FromSettings([
            Setting("chat-reply", ReplyDeliveryTarget.Chat),
            Setting("whisper-reply", ReplyDeliveryTarget.Whisper),
        ]);

        delivery.TargetFor("chat-reply").ShouldBe(CommandResponseTarget.Chat);
        delivery.TargetFor("whisper-reply").ShouldBe(CommandResponseTarget.Whisper);
    }

    [Test]
    public void SupportedPointLedgerKinds_Formatting_ReturnExpectedLabels()
    {
        (PointLedgerKind Kind, string Label)[] supportedKinds =
        [
            (PointLedgerKind.Add, "Points added"),
            (PointLedgerKind.Remove, "Points removed"),
            (PointLedgerKind.DeleteBalance, "Balance deleted"),
            (PointLedgerKind.TransferOut, "Points given"),
            (PointLedgerKind.TransferIn, "Points received"),
            (PointLedgerKind.GambleWin, "Gamble won"),
            (PointLedgerKind.GambleLoss, "Gamble lost"),
            (PointLedgerKind.GiveawayWin, "Giveaway prize"),
            (PointLedgerKind.GuessWin, "Guessing prize"),
            (PointLedgerKind.RequestReservation, "Request reserved"),
            (PointLedgerKind.RequestRefund, "Request refunded"),
        ];

        supportedKinds
            .Select(item => PointsDashboard.LedgerChangeLabel(item.Kind))
            .ShouldBe(supportedKinds.Select(item => item.Label));
    }

    [Test]
    public async Task InvalidReplyFeature_LoadingDelivery_ThrowsDataIntegrityFailure()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using (var seedDb = await dbFactory.CreateDbContextAsync())
        {
            seedDb.ReplyDeliverySettings.Add(
                new ReplyDeliverySetting
                {
                    HostId = hostId,
                    Feature = ReplyFeature.Guessing,
                    ReplyKey = "reply",
                    Target = ReplyDeliveryTarget.Whisper,
                }
            );
            await seedDb.SaveChangesAsync();
        }

        await using (var corruptionDb = await dbFactory.CreateDbContextAsync())
        {
            await corruptionDb.Database.OpenConnectionAsync();
            try
            {
                await corruptionDb.Database.ExecuteSqlRawAsync(
                    "PRAGMA ignore_check_constraints = ON"
                );
                await corruptionDb.Database.ExecuteSqlRawAsync(
                    "UPDATE reply_delivery_settings SET Feature = 'invalid-feature'"
                );
                await corruptionDb.Database.ExecuteSqlRawAsync(
                    "PRAGMA ignore_check_constraints = OFF"
                );
            }
            finally
            {
                await corruptionDb.Database.CloseConnectionAsync();
            }
        }

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var thrown = await Should.ThrowAsync<PersistenceDataIntegrityException>(() =>
            ReplyDeliverySettingWriter.LoadAsync(
                readDb,
                hostId,
                ReplyFeature.Guessing,
                ReplyDeliverySettingWriter.HostScopeId,
                CancellationToken.None
            )
        );
        thrown.DiscriminatorType.ShouldBe(typeof(ReplyFeature));
        thrown.Message.ShouldNotContain("invalid-feature");
    }

    [Test]
    public async Task SupportedPointOperations_Completing_WriteExpectedLedgerKinds()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var service = new PointBalanceService(dbFactory);

        AssertSuccess(
            await service
                .Add(hostId, "alpha", Amount(100), "moderator", "add")
                .ExecuteAsync(CancellationToken.None)
        );
        AssertSuccess(
            await service
                .Remove(hostId, "alpha", Amount(10), "moderator", "remove")
                .ExecuteAsync(CancellationToken.None)
        );
        AssertSuccess(
            await service
                .Add(hostId, "beta", Amount(20), "moderator", "add")
                .ExecuteAsync(CancellationToken.None)
        );
        AssertSuccess(
            await service
                .Transfer(hostId, "alpha", "beta", Amount(5))
                .ExecuteAsync(CancellationToken.None)
        );
        AssertSuccess(
            await service
                .ApplyGamble(hostId, "alpha", Amount(5), new PointGambleOutcome.Won())
                .ExecuteAsync(CancellationToken.None)
        );
        AssertSuccess(
            await service
                .ApplyGamble(hostId, "alpha", Amount(5), new PointGambleOutcome.Lost())
                .ExecuteAsync(CancellationToken.None)
        );

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var now = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
            AssertSuccess(
                await service
                    .AwardGiveaway(db, hostId, 42, "beta", Amount(10), now)
                    .ExecuteAsync(CancellationToken.None)
            );
            AssertSuccess(
                await service
                    .AwardGuessWin(db, hostId, 24, "alpha", Amount(10), now)
                    .ExecuteAsync(CancellationToken.None)
            );
            await db.SaveChangesAsync();
        }

        AssertSuccess(
            await service
                .DeleteBalance(hostId, "beta", "moderator", "delete")
                .ExecuteAsync(CancellationToken.None)
        );

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var kinds = await readDb
            .PointLedgerEntries.AsNoTracking()
            .Select(x => x.Kind)
            .Distinct()
            .ToListAsync();
        kinds
            .Order()
            .ShouldBe(
                new[]
                {
                    PointLedgerKind.Add,
                    PointLedgerKind.Remove,
                    PointLedgerKind.DeleteBalance,
                    PointLedgerKind.TransferOut,
                    PointLedgerKind.TransferIn,
                    PointLedgerKind.GambleWin,
                    PointLedgerKind.GambleLoss,
                    PointLedgerKind.GiveawayWin,
                    PointLedgerKind.GuessWin,
                }.Order()
            );
    }

    private static void AssertSuccess(
        Result<PointBalanceMutation, PointBalanceMutationFailure> result
    ) => result.Match(static _ => true, static _ => false).ShouldBeTrue();

    private static ReplyDeliverySetting Setting(string key, ReplyDeliveryTarget target) =>
        new()
        {
            Feature = ReplyFeature.Guessing,
            ReplyKey = key,
            Target = target,
        };

    private static PointAmount Amount(int value) => PointAmount.ParseAbsolute(value.ToString());

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc),
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
