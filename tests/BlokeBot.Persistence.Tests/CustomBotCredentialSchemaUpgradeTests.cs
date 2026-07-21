using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class CustomBotCredentialSchemaUpgradeTests
{
    [Test]
    public async Task LegacyPlaintextCredentials_Upgrading_DeletesDisablesAndAlerts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await CreateLegacySettingsAsync(dbFactory);

        await new CustomBotCredentialSchemaUpgrade(dbFactory).ApplyAsync(CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.HostBotAccountSettings.SingleAsync();
        var host = await db.Hosts.SingleAsync();
        var alert = await db.DurableAlerts.SingleAsync();
        var columns = await ColumnNamesAsync(db);
        settings.HostId.ShouldBe(hostId);
        settings.OverrideEnabled.ShouldBeFalse();
        settings.WhisperResponsesEnabled.ShouldBeFalse();
        settings.ProtectedTokenPayload.ShouldBeNull();
        settings.TwitchUserId.ShouldBeNull();
        settings.Login.ShouldBeNull();
        settings.AuthorizedAtUtc.ShouldBeNull();
        settings.AuthorizedScopes.ShouldBeNull();
        host.BotRuntimeState.ShouldBe(BotChannelRuntimeState.Stopped);
        alert.HostId.ShouldBe(hostId);
        alert.Source.ShouldBe(CustomBotCredentialAlert.Source);
        alert.SourceKey.ShouldBe(CustomBotCredentialAlert.SourceKey);
        alert.Message.ShouldBe(CustomBotCredentialAlert.Message);
        columns.ShouldContain("ProtectedTokenPayload");
        columns.ShouldNotContain("AccessToken");
        columns.ShouldNotContain("RefreshToken");
        columns.ShouldNotContain("ExpiresAtUtc");
    }

    private static async Task<int> CreateLegacySettingsAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            BotRuntimeState = BotChannelRuntimeState.Started,
            BotRuntimeStateChangedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            DisplayName = "Streamer",
            Login = "streamer",
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE host_bot_account_settings;
            CREATE TABLE host_bot_account_settings (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                HostId INTEGER NOT NULL,
                OverrideEnabled INTEGER NOT NULL,
                WhisperResponsesEnabled INTEGER NOT NULL,
                TwitchUserId TEXT NULL,
                Login TEXT NULL,
                DisplayName TEXT NULL,
                ProfileImageUrl TEXT NULL,
                AccessToken TEXT NULL,
                RefreshToken TEXT NULL,
                ExpiresAtUtc TEXT NULL,
                AuthorizedAtUtc TEXT NULL,
                AuthorizedScopes TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (HostId) REFERENCES hosts (Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IX_host_bot_account_settings_HostId
                ON host_bot_account_settings (HostId);
            INSERT INTO host_bot_account_settings
                (HostId, OverrideEnabled, WhisperResponsesEnabled, TwitchUserId, Login,
                 DisplayName, ProfileImageUrl, AccessToken, RefreshToken, ExpiresAtUtc,
                 AuthorizedAtUtc, AuthorizedScopes, UpdatedAtUtc)
            VALUES
                (@hostId, 1, 1, 'custom-id', 'custombot', 'Custom Bot',
                 'https://example.test/custom.png', 'legacy-access-token',
                 'legacy-refresh-token', @expiresAtUtc, @now, 'chat:read chat:edit', @now);
            """;
        command.Parameters.AddWithValue("@hostId", host.Id);
        command.Parameters.AddWithValue("@expiresAtUtc", DateTime.UtcNow.AddHours(1));
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync();
        return host.Id;
    }

    private static async Task<string[]> ColumnNamesAsync(BlokeBotDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(host_bot_account_settings);";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns.ToArray();
    }
}
