using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class OverlayAccessRegenerationMigrationTests
{
    private const string _releasedMigration = "20260822192152_v0.12.0_GuessingSharedAliases";

    [Test]
    public async Task Upgrade_PreservesExistingBrowserSourceCredentialsAsUsable()
    {
        const string ExistingKey = "migration-overlay-private-key-0000000000000";
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await using (var before = await database.CreateDbContextAsync())
        {
            await before.Database.MigrateAsync(_releasedMigration);
            var host = new BotHost
            {
                TwitchUserId = "migration-id",
                Login = "migration",
                DisplayName = "Migration",
                EnabledFeatures = HostFeatureFlags.Overlays,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = before.Hosts.Add(host);
            _ = await before.SaveChangesAsync();
            var publicId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var digest = OverlayAccessKeyDigest.Compute(ExistingKey);
            _ = await before.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO overlay_instances (
                    PublicId,
                    HostId,
                    Name,
                    Type,
                    IsEnabled,
                    ConfigurationJson,
                    AccessKeyDigest,
                    KeyVersion,
                    Revision,
                    CreatedAtUtc,
                    UpdatedAtUtc
                ) VALUES (
                    {publicId.ToString("D")},
                    {host.Id},
                    {"Existing source"},
                    {"empty"},
                    {true},
                    {"{\"schemaVersion\":1}"},
                    {digest},
                    {1},
                    {1L},
                    {now},
                    {now}
                );
                """
            );
        }

        await using (var migrate = await database.CreateDbContextAsync())
        {
            await migrate.Database.MigrateAsync();
            (await migrate.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }

        await using (var verify = await database.CreateDbContextAsync())
        {
            var overlay = await verify.OverlayInstances.AsNoTracking().SingleAsync();
            overlay.RequiresAccessKeyRegeneration.ShouldBeFalse();
            overlay.AccessKeyDigest.ShouldBe(OverlayAccessKeyDigest.Compute(ExistingKey));
        }
        _ = (
            await new OverlayInstanceResolver(database).ResolveAsync(
                ExistingKey,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlayResolutionResult.Resolved>();
    }
}
