using System.Text.Json;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationActivationMigrationTests
{
    private const string _preB262Migration = "20260825104453_v0.13.0_DurableAlertRecurrence";
    private const string _safeLegacyReason =
        "A previous automatic activation failed. Retry automatic activation.";

    [Test]
    public async Task Upgrade_PreservesEachLegacyFailureCodeWithASafeGenericReason()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await MigrateAsync(database, _preB262Migration);
        var hostId = await SeedHostAsync(database);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        const string FirstCode = "legacy-provider-one";
        const string SecondCode = "legacy-\"provider-two\"";
        await SeedLegacyActivationAsync(database, firstId, hostId, FirstCode);
        await SeedLegacyActivationAsync(database, secondId, hostId, SecondCode);

        await MigrateAsync(database);

        await using var verify = await database.CreateDbContextAsync();
        var rows = await verify
            .ConfigurationActivations.AsNoTracking()
            .Where(row => row.Id == firstId || row.Id == secondId)
            .ToDictionaryAsync(row => row.Id);
        var firstIssue = PersistedIssues(rows[firstId]).ShouldHaveSingleItem();
        var secondIssue = PersistedIssues(rows[secondId]).ShouldHaveSingleItem();
        firstIssue.Code.ShouldBe(FirstCode);
        secondIssue.Code.ShouldBe(SecondCode);
        firstIssue.Reason.ShouldBe(_safeLegacyReason);
        secondIssue.Reason.ShouldBe(_safeLegacyReason);
        firstIssue.Reason.ShouldNotContain(FirstCode);
        secondIssue.Reason.ShouldNotContain(SecondCode);
    }

    [Test]
    public async Task UpgradedManualFollowUp_DowngradesToFailedWithItsFirstIssueCode()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await MigrateAsync(database, _preB262Migration);
        var hostId = await SeedHostAsync(database);
        await MigrateAsync(database);
        var activationId = Guid.NewGuid();
        await using (var seed = await database.CreateDbContextAsync())
        {
            var now = DateTime.UtcNow;
            _ = seed.ConfigurationActivations.Add(
                new()
                {
                    Id = activationId,
                    HostId = hostId,
                    EnabledChanges = HostFeatureFlags.Polls,
                    Status = ConfigurationActivationStatus.ManualFollowUp,
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    IssuesJson = JsonSerializer.Serialize<PersistedIssue[]>([
                        new("manual-provider-one", "Reconnect the first provider."),
                        new("manual-provider-two", "Reconnect the second provider."),
                    ]),
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        await MigrateAsync(database, _preB262Migration);

        await using var verify = await database.CreateDbContextAsync();
        var connection = verify.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT \"Status\", \"FailureCode\" FROM \"configuration_activations\" "
            + "WHERE \"Id\" = $id;";
        var id = command.CreateParameter();
        id.ParameterName = "$id";
        id.Value = activationId.ToString();
        _ = command.Parameters.Add(id);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Failed");
        reader.GetString(1).ShouldBe("manual-provider-one");
        (await reader.ReadAsync()).ShouldBeFalse();
    }

    private static async Task MigrateAsync(
        SqliteBlokeBotDbFactory database,
        string? targetMigration = null
    )
    {
        await using var db = await database.CreateDbContextAsync();
        if (targetMigration is null)
        {
            await db.Database.MigrateAsync();
            return;
        }

        await db.Database.MigrateAsync(targetMigration);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = $"migration-{Guid.NewGuid():N}",
            DisplayName = "Migration host",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedLegacyActivationAsync(
        SqliteBlokeBotDbFactory database,
        Guid activationId,
        int hostId,
        string failureCode
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO configuration_activations (
                Id,
                HostId,
                EnabledChanges,
                DisabledChanges,
                Status,
                AttemptCount,
                Revision,
                CreatedAtUtc,
                UpdatedAtUtc,
                CompletedAtUtc,
                FailureCode
            ) VALUES (
                {activationId.ToString()},
                {hostId},
                {(long)HostFeatureFlags.Polls},
                0,
                'Failed',
                1,
                1,
                {now},
                {now},
                NULL,
                {failureCode}
            );
            """
        );
    }

    private static IReadOnlyList<PersistedIssue> PersistedIssues(
        ConfigurationActivation activation
    ) =>
        JsonSerializer.Deserialize<PersistedIssue[]>(activation.IssuesJson.ShouldNotBeNull()) ?? [];

    private sealed record PersistedIssue(string Code, string Reason);
}
