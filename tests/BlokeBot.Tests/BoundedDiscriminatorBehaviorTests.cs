using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

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
        ReplyDeliveryTarget.Chat.ToCommandTarget().ShouldBe(CommandResponseTarget.Chat);
        ReplyDeliveryTarget.Whisper.ToCommandTarget().ShouldBe(CommandResponseTarget.Whisper);
    }

    [Test]
    public void PointLedgerKinds_Formatting_CoversEveryKind()
    {
        Enum.GetValues<PointLedgerKind>()
            .Select(kind => (kind, PointsDashboard.LedgerChangeLabel(kind)))
            .ShouldBe([
                (PointLedgerKind.Add, "Points added"),
                (PointLedgerKind.Remove, "Points removed"),
                (PointLedgerKind.DeleteBalance, "Balance deleted"),
                (PointLedgerKind.TransferOut, "Points given"),
                (PointLedgerKind.TransferIn, "Points received"),
                (PointLedgerKind.GambleWin, "Gamble won"),
                (PointLedgerKind.GambleLoss, "Gamble lost"),
                (PointLedgerKind.GiveawayWin, "Giveaway prize"),
                (PointLedgerKind.GuessWin, "Guessing prize"),
            ]);
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
    public async Task PointOperations_Completing_WritesEveryLedgerKind()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var service = new PointBalanceService(dbFactory);

        (
            await service.AddAsync(
                hostId,
                "alpha",
                Amount(100),
                "moderator",
                "add",
                CancellationToken.None
            )
        ).Success.ShouldBeTrue();
        (
            await service.RemoveAsync(
                hostId,
                "alpha",
                Amount(10),
                "moderator",
                "remove",
                CancellationToken.None
            )
        ).Success.ShouldBeTrue();
        (
            await service.AddAsync(
                hostId,
                "beta",
                Amount(20),
                "moderator",
                "add",
                CancellationToken.None
            )
        ).Success.ShouldBeTrue();
        (
            await service.TransferAsync(hostId, "alpha", "beta", Amount(5), CancellationToken.None)
        ).Success.ShouldBeTrue();
        (
            await service.ApplyGambleAsync(hostId, "alpha", Amount(5), true, CancellationToken.None)
        ).Success.ShouldBeTrue();
        (
            await service.ApplyGambleAsync(
                hostId,
                "alpha",
                Amount(5),
                false,
                CancellationToken.None
            )
        ).Success.ShouldBeTrue();

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var now = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
            (
                await service.AwardGiveawayAsync(
                    db,
                    hostId,
                    42,
                    "beta",
                    Amount(10),
                    now,
                    CancellationToken.None
                )
            ).Success.ShouldBeTrue();
            (
                await service.AwardGuessWinAsync(
                    db,
                    hostId,
                    24,
                    "alpha",
                    Amount(10),
                    now,
                    CancellationToken.None
                )
            ).Success.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        (
            await service.DeleteBalanceAsync(
                hostId,
                "beta",
                "moderator",
                "delete",
                CancellationToken.None
            )
        ).Success.ShouldBeTrue();

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var kinds = await readDb
            .PointLedgerEntries.AsNoTracking()
            .Select(x => x.Kind)
            .Distinct()
            .ToListAsync();
        kinds.Order().ShouldBe(Enum.GetValues<PointLedgerKind>());
    }

    private static ReplyDeliverySetting Setting(string key, ReplyDeliveryTarget target)
    {
        return new()
        {
            Feature = ReplyFeature.Guessing,
            ReplyKey = key,
            Target = target,
        };
    }

    private static PointAmount Amount(int value)
    {
        return PointAmount.ParseAbsolute(value.ToString());
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc),
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
