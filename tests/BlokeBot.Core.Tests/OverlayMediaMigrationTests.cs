using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class OverlayMediaMigrationTests
{
    private const string _previousMigration = "20260815134407_v0.11.0_AutomationNodeDisplayAliases";

    [Test]
    public async Task LegacyMedia_MigratesToImmutableDocumentsAndRecoversOnlyPresentFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"blokebot-media-migration-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "blokebot.db");
        _ = Directory.CreateDirectory(root);
        try
        {
            await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
            var (hostId, presentId, presentKey, missingId) = await SeedLegacyAsync(database);
            var legacyDirectory = OverlayMediaDirectory.HostDirectory(root, hostId);
            _ = Directory.CreateDirectory(legacyDirectory);
            await File.WriteAllBytesAsync(Path.Combine(legacyDirectory, presentKey), [1, 2, 3]);

            await new BlokeBotDatabaseInitializer(database).InitializeAsync(CancellationToken.None);
            var maintenance = new OverlayMediaMaintenanceService(
                database,
                Options.Create(new BlokeBotOptions { DatabasePath = databasePath }),
                new SystemOverlayMediaFileDeletion(),
                TimeProvider.System,
                NullLogger<OverlayMediaMaintenanceService>.Instance
            );
            await maintenance.RecoverAsync(CancellationToken.None);

            await using var verify = await database.CreateDbContextAsync();
            var references = await verify
                .OverlayMediaAssets.Include(value => value.Document)
                .OrderBy(value => value.Name)
                .ToArrayAsync();
            references.Length.ShouldBe(2);
            references
                .Single(value => value.PublicId == presentId)
                .Document.State.ShouldBe(OverlayMediaDocumentState.Available);
            references
                .Single(value => value.PublicId == missingId)
                .Document.State.ShouldBe(OverlayMediaDocumentState.Unavailable);
            File.Exists(Path.Combine(OverlayMediaDirectory.DocumentDirectory(root), presentKey))
                .ShouldBeTrue();
            File.Exists(Path.Combine(legacyDirectory, presentKey)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(
        int HostId,
        Guid PresentId,
        string PresentKey,
        Guid MissingId
    )> SeedLegacyAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        await db.GetService<IMigrator>().MigrateAsync(_previousMigration);
        var host = new BotHost
        {
            TwitchUserId = "migration-id",
            Login = "migration",
            DisplayName = "Migration",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var presentId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var presentKey = new string('a', 32);
        var missingKey = new string('b', 32);
        var now = DateTime.UtcNow;
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO overlay_media_assets
                (PublicId, HostId, Name, ContentRevision, ContentType, ByteLength, StorageKey, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ({presentId.ToString(
                "D"
            )}, {host.Id}, {"Present"}, {1}, {"video/mp4"}, {3L}, {presentKey}, {now}, {now}),
                ({missingId.ToString(
                "D"
            )}, {host.Id}, {"Missing"}, {1}, {"video/mp4"}, {3L}, {missingKey}, {now}, {now});
            """
        );
        return (host.Id, presentId, presentKey, missingId);
    }
}
