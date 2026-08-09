using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class BoundedDiscriminatorPersistenceTests
{
    [Test]
    public async Task InvalidReplyFeature_Persisted_Querying_ThrowsDataIntegrityFailure() =>
        await AssertCorruptionFailureAsync(
            "UPDATE reply_delivery_settings SET Feature = 'invalid-feature'",
            typeof(ReplyFeature),
            static async db => _ = await db.ReplyDeliverySettings.AsNoTracking().SingleAsync()
        );

    [Test]
    public async Task InvalidReplyTarget_Persisted_Querying_ThrowsDataIntegrityFailure() =>
        await AssertCorruptionFailureAsync(
            "UPDATE reply_delivery_settings SET Target = 'invalid-target'",
            typeof(ReplyDeliveryTarget),
            static async db => _ = await db.ReplyDeliverySettings.AsNoTracking().SingleAsync()
        );

    [Test]
    public async Task InvalidGuessOptionTarget_Persisted_Querying_ThrowsDataIntegrityFailure() =>
        await AssertCorruptionFailureAsync(
            "UPDATE guess_options SET ReplyTarget = 'invalid-target'",
            typeof(ReplyDeliveryTarget),
            static async db => _ = await db.GuessOptions.AsNoTracking().SingleAsync()
        );

    [Test]
    public async Task InvalidPointLedgerKind_Persisted_Querying_ThrowsDataIntegrityFailure() =>
        await AssertCorruptionFailureAsync(
            "UPDATE point_ledger_entries SET Kind = 'invalid-kind'",
            typeof(PointLedgerKind),
            static async db => _ = await db.PointLedgerEntries.AsNoTracking().SingleAsync()
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
