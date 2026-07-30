using System.Data.Common;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class NativeTwitchMigrationTests
{
    private const string _publishedV03 = "20260726161453_v0.3.0";
    private const string _nativeTwitchFeatureSwitch =
        "20260728201821_v0.3.0_NativeTwitchFeatureSwitch";
    private const string _automaticRaidShoutouts = "20260729101929_v0.3.0_AutomaticRaidShoutouts";

    [Test]
    public async Task NativeTwitchFeatureSwitch_SeededUpgrade_PreservesCapabilitiesMasksAndFinalSchema()
    {
        await using var upgradedFactory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var published = await upgradedFactory.CreateDbContextAsync())
        {
            await published.GetService<IMigrator>().MigrateAsync(_nativeTwitchFeatureSwitch);
            published.Hosts.AddRange(Host("seven", 7), Host("other-bits", 23));
            published.TwitchCustomRewards.Add(
                new TwitchCustomReward
                {
                    HostId = 1,
                    ProviderRewardId = "reward",
                    Title = "Reward",
                    Cost = 100,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            published.TwitchRewardRedemptions.Add(
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
            published.TwitchPredictionTemplates.Add(
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
            published.TwitchPredictions.Add(
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
            await published.SaveChangesAsync();
            await published.Database.MigrateAsync();
        }

        IReadOnlyList<string> upgradedSchema;
        await using (var upgraded = await upgradedFactory.CreateDbContextAsync())
        {
            (
                await upgraded
                    .Hosts.OrderBy(host => host.Id)
                    .Select(host => (long)host.EnabledFeatures)
                    .ToArrayAsync()
            ).ShouldBe([23L, 23L]);
            (
                await upgraded
                    .TwitchCustomRewards.Select(value => value.ProviderRewardId)
                    .ToArrayAsync()
            ).ShouldBe(["reward"]);
            (
                await upgraded
                    .TwitchRewardRedemptions.Select(value => value.ProviderRedemptionId)
                    .ToArrayAsync()
            ).ShouldBe(["redemption"]);
            (
                await upgraded.TwitchPredictionTemplates.Select(value => value.Title).ToArrayAsync()
            ).ShouldBe(["Template"]);
            (
                await upgraded
                    .TwitchPredictionTemplateOutcomes.OrderBy(value => value.Position)
                    .Select(value => value.Title)
                    .ToArrayAsync()
            ).ShouldBe(["Yes", "No"]);
            (
                await upgraded
                    .TwitchPredictions.Select(value => value.ProviderPredictionId)
                    .ToArrayAsync()
            ).ShouldBe(["prediction"]);

            var history = await ReadColumnAsync(
                upgraded.Database.GetDbConnection(),
                """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";"""
            );
            history.ShouldContain(_publishedV03);
            history.ShouldContain(_nativeTwitchFeatureSwitch);
            history.ShouldContain(_automaticRaidShoutouts);
            history.Count.ShouldBe(12);
            history.ShouldNotContain("20260726031743_v0.3.0");
            history.ShouldNotContain("20260728183253_v0.4.0");
            upgradedSchema = await ReadSchemaAsync(upgraded.Database.GetDbConnection());
        }

        await using var freshFactory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using var fresh = await freshFactory.CreateDbContextAsync();
        await fresh.Database.MigrateAsync();
        var freshSchema = await ReadSchemaAsync(fresh.Database.GetDbConnection());

        upgradedSchema.ShouldBe(freshSchema);
    }

    private static BotHost Host(string login, ulong features)
    {
        return new()
        {
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            EnabledFeatures = (HostFeatureFlags)features,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    private static Task<IReadOnlyList<string>> ReadSchemaAsync(DbConnection connection)
    {
        return ReadColumnAsync(
            connection,
            """
            SELECT type || '|' || name || '|' || tbl_name || '|' || COALESCE(sql, '')
            FROM sqlite_master
            WHERE name NOT LIKE 'sqlite_%'
              AND name <> '__EFMigrationsHistory'
            ORDER BY type, name;
            """
        );
    }

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
