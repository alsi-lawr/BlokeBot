using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class BoundedDiscriminatorPersistenceTests
{
    [Test]
    public async Task ValidDiscriminators_Persisting_RoundTripsEveryCanonicalToken()
    {
        PointLedgerKind[] supportedLedgerKinds =
        [
            PointLedgerKind.Add,
            PointLedgerKind.Remove,
            PointLedgerKind.DeleteBalance,
            PointLedgerKind.TransferOut,
            PointLedgerKind.TransferIn,
            PointLedgerKind.GambleWin,
            PointLedgerKind.GambleLoss,
            PointLedgerKind.GiveawayWin,
            PointLedgerKind.GuessWin,
            PointLedgerKind.RequestReservation,
            PointLedgerKind.RequestRefund,
        ];
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var host = Host();
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();

            var profile = Profile(host.Id);
            _ = db.Profiles.Add(profile);
            _ = await db.SaveChangesAsync();

            db.ReplyDeliverySettings.AddRange(
                ReplySetting(
                    host.Id,
                    ReplyFeature.Guessing,
                    "guessing-chat",
                    ReplyDeliveryTarget.Chat
                ),
                ReplySetting(
                    host.Id,
                    ReplyFeature.Points,
                    "points-whisper",
                    ReplyDeliveryTarget.Whisper
                )
            );
            db.GuessOptions.AddRange(
                GuessOption(profile.Id, "chat", ReplyDeliveryTarget.Chat),
                GuessOption(profile.Id, "whisper", ReplyDeliveryTarget.Whisper)
            );
            db.PointLedgerEntries.AddRange(
                supportedLedgerKinds.Select(kind => LedgerEntry(host.Id, kind))
            );
            _ = await db.SaveChangesAsync();
        }

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var replySettings = await readDb
            .ReplyDeliverySettings.AsNoTracking()
            .OrderBy(x => x.ReplyKey)
            .Select(x => new { x.Feature, x.Target })
            .ToListAsync();
        replySettings
            .Select(x => (x.Feature, x.Target))
            .ShouldBe([
                (ReplyFeature.Guessing, ReplyDeliveryTarget.Chat),
                (ReplyFeature.Points, ReplyDeliveryTarget.Whisper),
            ]);

        var guessTargets = await readDb
            .GuessOptions.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => x.ReplyTarget)
            .ToListAsync();
        guessTargets.ShouldBe([ReplyDeliveryTarget.Chat, ReplyDeliveryTarget.Whisper]);

        var ledgerKinds = await readDb
            .PointLedgerEntries.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => x.Kind)
            .ToListAsync();
        ledgerKinds.ShouldBe(supportedLedgerKinds);

        var featureTokens = await readDb
            .Database.SqlQueryRaw<string>(
                "SELECT Feature AS Value FROM reply_delivery_settings ORDER BY ReplyKey"
            )
            .ToListAsync();
        featureTokens.ShouldBe(["guessing", "points"]);

        var targetTokens = await readDb
            .Database.SqlQueryRaw<string>(
                "SELECT Target AS Value FROM reply_delivery_settings ORDER BY ReplyKey"
            )
            .ToListAsync();
        targetTokens.ShouldBe(["chat", "whisper"]);

        var guessTargetTokens = await readDb
            .Database.SqlQueryRaw<string>(
                "SELECT ReplyTarget AS Value FROM guess_options ORDER BY Name"
            )
            .ToListAsync();
        guessTargetTokens.ShouldBe(["chat", "whisper"]);

        var ledgerKindTokens = await readDb
            .Database.SqlQueryRaw<string>(
                "SELECT Kind AS Value FROM point_ledger_entries ORDER BY Id"
            )
            .ToListAsync();
        ledgerKindTokens.ShouldBe([
            "Add",
            "Remove",
            "DeleteBalance",
            "TransferOut",
            "TransferIn",
            "GambleWin",
            "GambleLoss",
            "GiveawayWin",
            "GuessWin",
            "RequestReservation",
            "RequestRefund",
        ]);
    }

    [Test]
    public async Task InvalidReplyFeature_Persisted_Querying_ThrowsDataIntegrityFailure() =>
        await AssertCorruptionFailureAsync(
            "UPDATE reply_delivery_settings SET Feature = 'invalid-feature'",
            typeof(ReplyFeature),
            async db => _ = await db.ReplyDeliverySettings.AsNoTracking().SingleAsync()
        );

    [Test]
    public async Task InvalidReplyTarget_Persisted_Querying_ThrowsDataIntegrityFailure() =>
        await AssertCorruptionFailureAsync(
            "UPDATE reply_delivery_settings SET Target = 'invalid-target'",
            typeof(ReplyDeliveryTarget),
            async db => _ = await db.ReplyDeliverySettings.AsNoTracking().SingleAsync()
        );

    [Test]
    public async Task InvalidGuessOptionTarget_Persisted_Querying_ThrowsDataIntegrityFailure() =>
        await AssertCorruptionFailureAsync(
            "UPDATE guess_options SET ReplyTarget = 'invalid-target'",
            typeof(ReplyDeliveryTarget),
            async db => _ = await db.GuessOptions.AsNoTracking().SingleAsync()
        );

    [Test]
    public async Task InvalidPointLedgerKind_Persisted_Querying_ThrowsDataIntegrityFailure() =>
        await AssertCorruptionFailureAsync(
            "UPDATE point_ledger_entries SET Kind = 'invalid-kind'",
            typeof(PointLedgerKind),
            async db => _ = await db.PointLedgerEntries.AsNoTracking().SingleAsync()
        );

    private static async Task AssertCorruptionFailureAsync(
        string corruptionSql,
        Type discriminatorType,
        Func<BlokeBotDbContext, Task> query
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedCorruptionRowsAsync(dbFactory);
        await CorruptAsync(dbFactory, corruptionSql);

        await using var readDb = await dbFactory.CreateDbContextAsync();
        var thrown = await Should.ThrowAsync<PersistenceDataIntegrityException>(() =>
            query(readDb)
        );
        thrown.DiscriminatorType.ShouldBe(discriminatorType);
        thrown.Message.ShouldNotContain("invalid-");
    }

    private static async Task SeedCorruptionRowsAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = Host();
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();

        var profile = Profile(host.Id);
        _ = db.Profiles.Add(profile);
        _ = await db.SaveChangesAsync();

        _ = db.ReplyDeliverySettings.Add(
            ReplySetting(host.Id, ReplyFeature.Guessing, "reply", ReplyDeliveryTarget.Chat)
        );
        _ = db.GuessOptions.Add(GuessOption(profile.Id, "guess", ReplyDeliveryTarget.Chat));
        _ = db.PointLedgerEntries.Add(LedgerEntry(host.Id, PointLedgerKind.Add));
        _ = await db.SaveChangesAsync();
    }

    private static async Task CorruptAsync(SqliteBlokeBotDbFactory dbFactory, string corruptionSql)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        try
        {
            _ = await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
            _ = await db.Database.ExecuteSqlRawAsync(corruptionSql);
            _ = await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static BotHost Host() =>
        new()
        {
            Login = $"host-{Guid.NewGuid():N}",
            DisplayName = "Host",
            CreatedAtUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc),
        };

    private static GuessRoundProfile Profile(int hostId) =>
        new()
        {
            HostId = hostId,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
        };

    private static ReplyDeliverySetting ReplySetting(
        int hostId,
        ReplyFeature feature,
        string replyKey,
        ReplyDeliveryTarget target
    ) =>
        new()
        {
            HostId = hostId,
            Feature = feature,
            ScopeId = 0,
            ReplyKey = replyKey,
            Target = target,
        };

    private static GuessOption GuessOption(
        int profileId,
        string name,
        ReplyDeliveryTarget target
    ) =>
        new()
        {
            GuessRoundProfileId = profileId,
            Name = name,
            ReplyText = name,
            ReplyTarget = target,
        };

    private static PointLedgerEntry LedgerEntry(int hostId, PointLedgerKind kind) =>
        new()
        {
            HostId = hostId,
            CreatedAtUtc = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc),
            Kind = kind,
            Login = "viewer",
        };
}
