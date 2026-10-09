using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class WatchTimeMigrationTests
{
    [Test]
    public async Task Sqlite_ExistingPointsUpgradeStartsOffAndPreservesBalanceAndLedger()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await using (var before = database.CreateDbContext())
        {
            await before.Database.MigrateAsync("20261003212945_AutomationSourceLifecycle");
            var host = new BotHost
            {
                Login = "legacy",
                DisplayName = "Legacy",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = before.Hosts.Add(host);
            _ = await before.SaveChangesAsync();
            var legacyEntry = before.Entry(
                new PointsSettings { HostId = host.Id, PointLabel = "legacy coins" }
            );
            var properties = legacyEntry
                .Properties.Where(value =>
                    value.Metadata.Name != nameof(PointsSettings.Id)
                    && !value.Metadata.Name.StartsWith("WatchTime", StringComparison.Ordinal)
                )
                .ToArray();
            var names = string.Join(
                ",",
                properties.Select(value => $"\"{value.Metadata.GetColumnName()}\"")
            );
            var parameters = properties
                .Select(
                    (value, index) =>
                        new SqliteParameter(
                            $"p{index}",
                            value
                                .Metadata.GetTypeMapping()
                                .Converter?.ConvertToProvider(value.CurrentValue)
                                ?? value.CurrentValue
                                ?? DBNull.Value
                        )
                )
                .ToArray();
            await before.Database.OpenConnectionAsync();
            await using (var command = before.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText =
                    $"INSERT INTO points_settings ({names}) VALUES ({string.Join(",", parameters.Select(value => "@" + value.ParameterName))})";
                foreach (var parameter in parameters)
                {
                    _ = command.Parameters.Add(parameter);
                }
                _ = await command.ExecuteNonQueryAsync();
            }
            await before.Database.CloseConnectionAsync();
            _ = before.PointBalances.Add(
                new()
                {
                    HostId = host.Id,
                    Login = "viewer",
                    Amount = "123456789012345678901234567890",
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = before.PointLedgerEntries.Add(
                new()
                {
                    HostId = host.Id,
                    Login = "viewer",
                    Kind = PointLedgerKind.Add,
                    Delta = "123456789012345678901234567890",
                    BalanceAfter = "123456789012345678901234567890",
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await before.SaveChangesAsync();
        }
        await using (var migrate = database.CreateDbContext())
        {
            await migrate.Database.MigrateAsync();
        }
        await using var verify = database.CreateDbContext();
        var settings = await verify.PointsSettings.SingleAsync();
        settings.PointLabel.ShouldBe("legacy coins");
        settings.WatchTimePointsEnabled.ShouldBeFalse();
        settings.WatchTimePointAmount.ShouldBeNull();
        settings.WatchTimeEnableGeneration.ShouldBe(Guid.Empty);
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe(
            "123456789012345678901234567890"
        );
        (await verify.PointLedgerEntries.SingleAsync()).BalanceAfter.ShouldBe(
            "123456789012345678901234567890"
        );
        await using var tx = await verify.Database.BeginTransactionAsync();
        var failure = await Should.ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            verify.Database.ExecuteSqlRawAsync(
                "UPDATE points_settings SET \"WatchTimePointsEnabled\"=1"
            )
        );
        failure.SqliteErrorCode.ShouldBe(19);
        await tx.RollbackAsync();
    }
}
