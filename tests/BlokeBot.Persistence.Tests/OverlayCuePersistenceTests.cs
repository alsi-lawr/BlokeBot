using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class OverlayCuePersistenceTests
{
    private const string _migration = "20260731064005_v0.6.0_CustomCommandOverlayCues";
    private const string _latestMigration = "20260731141254_v0.6.0_OverlayAppearance";

    [Test]
    public async Task Migration_AddsCueMediaAndHostBoundReferenceSchema()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        db.GetService<IMigrationsAssembly>().Migrations.Count.ShouldBe(20);
        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(_migration);
        (await db.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(_latestMigration);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();

        var first = Host("first");
        var second = Host("second");
        db.Hosts.AddRange(first, second);
        await db.SaveChangesAsync();
        var cue = Cue(first.Id);
        var asset = Asset(first.Id);
        db.OverlayCues.Add(cue);
        db.OverlayMediaAssets.Add(asset);
        await db.SaveChangesAsync();
        db.OverlayCueMediaAssetReferences.Add(
            new()
            {
                CueId = cue.Id,
                AssetId = asset.Id,
                HostId = first.Id,
            }
        );
        await db.SaveChangesAsync();

        await Should.ThrowAsync<SqliteException>(() =>
            db.OverlayMediaAssets.Where(value => value.Id == asset.Id).ExecuteDeleteAsync()
        );
        db.ChangeTracker.Clear();

        db.OverlayCueMediaAssetReferences.Add(
            new()
            {
                CueId = cue.Id,
                AssetId = asset.Id,
                HostId = second.Id,
            }
        );
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static BotHost Host(string login) =>
        new()
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static OverlayCue Cue(int hostId) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            Name = "Cue",
            IsEnabled = true,
            DurationMilliseconds = 1000,
            QueuePolicy = OverlayCueQueuePolicy.Enqueue,
            ConfigurationJson =
                """{"schemaVersion":1,"layers":[{"type":"uploadedMedia","assetId":"7a90d36d-e77c-496b-9211-d0547759050f","mediaKind":"video","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":0,"volume":1,"fit":"contain","rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}}]}""",
            Revision = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private static OverlayMediaAsset Asset(int hostId) =>
        new()
        {
            PublicId = Guid.Parse("7a90d36d-e77c-496b-9211-d0547759050f"),
            HostId = hostId,
            Name = "Asset",
            ContentType = "video/mp4",
            ByteLength = 12,
            ContentRevision = 1,
            StorageKey = "0123456789abcdef0123456789abcdef",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
}
