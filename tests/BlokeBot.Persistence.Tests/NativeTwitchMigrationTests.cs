using System.Data.Common;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class NativeTwitchMigrationTests
{
    private const string _nativeTwitchFeatureSwitch =
        "20260728201821_v0.3.0_NativeTwitchFeatureSwitch";

    [Test]
    public async Task NativeTwitchFeatureSwitch_SeededUpgrade_PreservesCapabilitiesMasksAndFinalSchema()
    {
        await using var upgradedFactory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var published = await upgradedFactory.CreateDbContextAsync())
        {
            await published.GetService<IMigrator>().MigrateAsync(_nativeTwitchFeatureSwitch);
            _ = await published.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures, CreatedAtUtc)
                VALUES
                    (1, 'seven-id', 'seven', 'seven', 0, 7, '2026-07-30T00:00:00Z'),
                    (2, 'other-bits-id', 'other-bits', 'other-bits', 0, 23, '2026-07-30T00:00:00Z');
                """
            );
            _ = published.TwitchCustomRewards.Add(
                new TwitchCustomReward
                {
                    HostId = 1,
                    ProviderRewardId = "reward",
                    Title = "Reward",
                    Cost = 100,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = published.TwitchRewardRedemptions.Add(
                new TwitchRewardRedemption
                {
                    HostId = 1,
                    ProviderRedemptionId = "redemption",
                    ProviderRewardId = "reward",
                    RewardTitle = "Reward",
                    UserId = "viewer-id",
                    UserLogin = "viewer",
                    Status = TwitchRewardRedemptionStatus.Unfulfilled,
                    RedeemedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = published.TwitchPredictionTemplates.Add(
                new TwitchPredictionTemplate
                {
                    HostId = 1,
                    Title = "Template",
                    PredictionWindowSeconds = 60,
                    CreatedAtUtc = DateTime.UtcNow,
                    Outcomes =
                    [
                        new() { Position = 0, Title = "Yes" },
                        new() { Position = 1, Title = "No" },
                    ],
                }
            );
            _ = published.TwitchPredictions.Add(
                new TwitchPrediction
                {
                    HostId = 1,
                    ProviderPredictionId = "prediction",
                    Title = "Prediction",
                    OutcomesJson = "[]",
                    Status = TwitchPredictionStatus.Active,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await published.SaveChangesAsync();
            await published.Database.MigrateAsync();
        }

        IReadOnlyList<string> upgradedSchema;
        await using (var upgraded = await upgradedFactory.CreateDbContextAsync())
        {
            (
                await upgraded
                    .Hosts.OrderBy(static host => host.Id)
                    .Select(static host => (long)host.EnabledFeatures)
                    .ToArrayAsync()
            ).ShouldBe([247L, 247L]);
            (
                await upgraded
                    .TwitchCustomRewards.Select(static value => value.ProviderRewardId)
                    .ToArrayAsync()
            ).ShouldBe(["reward"]);
            (
                await upgraded
                    .TwitchRewardRedemptions.Select(static value => value.ProviderRedemptionId)
                    .ToArrayAsync()
            ).ShouldBe(["redemption"]);
            (
                await upgraded
                    .TwitchPredictionTemplates.Select(static value => value.Title)
                    .ToArrayAsync()
            ).ShouldBe(["Template"]);
            (
                await upgraded
                    .TwitchPredictionTemplateOutcomes.OrderBy(static value => value.Position)
                    .Select(static value => value.Title)
                    .ToArrayAsync()
            ).ShouldBe(["Yes", "No"]);
            (
                await upgraded
                    .TwitchPredictions.Select(static value => value.ProviderPredictionId)
                    .ToArrayAsync()
            ).ShouldBe(["prediction"]);

            upgradedSchema = await ReadSchemaAsync(upgraded.Database.GetDbConnection());
        }

        await using var freshFactory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using var fresh = await freshFactory.CreateDbContextAsync();
        await fresh.Database.MigrateAsync();
        var freshSchema = await ReadSchemaAsync(fresh.Database.GetDbConnection());

        upgradedSchema.ShouldBe(freshSchema);
    }

    private static Task<IReadOnlyList<string>> ReadSchemaAsync(DbConnection connection) =>
        ReadColumnAsync(
            connection,
            """
            SELECT type || '|' || name || '|' || tbl_name || '|' || COALESCE(sql, '')
            FROM sqlite_master
            WHERE name NOT LIKE 'sqlite_%'
              AND name <> '__EFMigrationsHistory'
            ORDER BY type, name;
            """
        );

    private static async Task<IReadOnlyList<string>> ReadColumnAsync(
        DbConnection connection,
        string sql
    )
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }
        return values;
    }
}
