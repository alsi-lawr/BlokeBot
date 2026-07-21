using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

internal sealed class CustomBotCredentialSchemaUpgrade
{
    private const string _settingsTable = "host_bot_account_settings";

    private static readonly string[] _requiredSettingsColumns =
    [
        "Id",
        "HostId",
        "OverrideEnabled",
        "WhisperResponsesEnabled",
        "TwitchUserId",
        "Login",
        "DisplayName",
        "ProfileImageUrl",
        "AuthorizedAtUtc",
        "AuthorizedScopes",
        "UpdatedAtUtc",
    ];

    private static readonly string[] _legacyCredentialColumns =
    [
        "AccessToken",
        "RefreshToken",
        "ExpiresAtUtc",
    ];

    private readonly IDbContextFactory<BlokeBotDbContext> _dbFactory;
    private readonly TimeProvider _timeProvider;

    internal CustomBotCredentialSchemaUpgrade(IDbContextFactory<BlokeBotDbContext> dbFactory)
        : this(dbFactory, TimeProvider.System) { }

    internal CustomBotCredentialSchemaUpgrade(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        TimeProvider timeProvider
    )
    {
        _dbFactory = dbFactory;
        _timeProvider = timeProvider;
    }

    internal async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        var columns = await ExistingColumnsAsync(connection, cancellationToken);
        if (columns.Count == 0)
        {
            return;
        }

        EnsureRequiredColumns(columns);
        if (columns.Contains("ProtectedTokenPayload"))
        {
            await TruncateWalAsync(connection, cancellationToken);
            return;
        }

        if (_legacyCredentialColumns.Any(column => !columns.Contains(column)))
        {
            throw new PersistenceDataIntegrityException(typeof(Models.HostBotAccountSettings));
        }

        await EnableSecureDeleteAsync(connection, cancellationToken);
        await RebuildSettingsAsync(connection, cancellationToken);
        await TruncateWalAsync(connection, cancellationToken);
    }

    private async Task RebuildSettingsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        const string Affected = "(AccessToken IS NOT NULL OR RefreshToken IS NOT NULL)";
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"""
            INSERT OR IGNORE INTO durable_alerts
                (HostId, Severity, Source, SourceKey, Title, Message, LinkPath,
                 CreatedAtUtc, AcknowledgedAtUtc, AcknowledgedByLogin)
            SELECT HostId, 'Warning', @alertSource, @alertSourceKey, @alertTitle,
                   @alertMessage, @alertLinkPath, @now, NULL, NULL
            FROM host_bot_account_settings
            WHERE {Affected};

            UPDATE hosts
            SET BotRuntimeState = 0,
                BotRuntimeStateChangedAtUtc = @now
            WHERE Id IN (
                SELECT HostId
                FROM host_bot_account_settings
                WHERE {Affected}
            );

            DROP TABLE IF EXISTS host_bot_account_settings_credential_upgrade;

            CREATE TABLE host_bot_account_settings_credential_upgrade (
                Id INTEGER NOT NULL
                    CONSTRAINT PK_host_bot_account_settings PRIMARY KEY AUTOINCREMENT,
                HostId INTEGER NOT NULL,
                OverrideEnabled INTEGER NOT NULL,
                WhisperResponsesEnabled INTEGER NOT NULL,
                TwitchUserId TEXT NULL,
                Login TEXT NULL,
                DisplayName TEXT NULL,
                ProfileImageUrl TEXT NULL,
                ProtectedTokenPayload BLOB NULL,
                AuthorizedAtUtc TEXT NULL,
                AuthorizedScopes TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                CONSTRAINT FK_host_bot_account_settings_hosts_HostId
                    FOREIGN KEY (HostId) REFERENCES hosts (Id) ON DELETE CASCADE
            );

            INSERT INTO host_bot_account_settings_credential_upgrade
                (Id, HostId, OverrideEnabled, WhisperResponsesEnabled,
                 TwitchUserId, Login, DisplayName, ProfileImageUrl,
                 ProtectedTokenPayload, AuthorizedAtUtc, AuthorizedScopes, UpdatedAtUtc)
            SELECT Id,
                   HostId,
                   CASE WHEN {Affected} THEN 0 ELSE OverrideEnabled END,
                   CASE WHEN {Affected} THEN 0 ELSE WhisperResponsesEnabled END,
                   CASE WHEN {Affected} THEN NULL ELSE TwitchUserId END,
                   CASE WHEN {Affected} THEN NULL ELSE Login END,
                   CASE WHEN {Affected} THEN NULL ELSE DisplayName END,
                   CASE WHEN {Affected} THEN NULL ELSE ProfileImageUrl END,
                   NULL,
                   CASE WHEN {Affected} THEN NULL ELSE AuthorizedAtUtc END,
                   CASE WHEN {Affected} THEN NULL ELSE AuthorizedScopes END,
                   CASE WHEN {Affected} THEN @now ELSE UpdatedAtUtc END
            FROM host_bot_account_settings;

            DROP TABLE host_bot_account_settings;
            ALTER TABLE host_bot_account_settings_credential_upgrade
                RENAME TO host_bot_account_settings;
            CREATE UNIQUE INDEX IX_host_bot_account_settings_HostId
                ON host_bot_account_settings (HostId);

            """;
        command.Parameters.AddWithValue("@alertSource", CustomBotCredentialAlert.Source);
        command.Parameters.AddWithValue("@alertSourceKey", CustomBotCredentialAlert.SourceKey);
        command.Parameters.AddWithValue("@alertTitle", CustomBotCredentialAlert.Title);
        command.Parameters.AddWithValue("@alertMessage", CustomBotCredentialAlert.Message);
        command.Parameters.AddWithValue("@alertLinkPath", CustomBotCredentialAlert.LinkPath);
        command.Parameters.AddWithValue("@now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void EnsureRequiredColumns(IReadOnlySet<string> columns)
    {
        if (_requiredSettingsColumns.Any(column => !columns.Contains(column)))
        {
            throw new PersistenceDataIntegrityException(typeof(Models.HostBotAccountSettings));
        }
    }

    private static async Task<HashSet<string>> ExistingColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({_settingsTable});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        return columns;
    }

    private static async Task EnableSecureDeleteAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task TruncateWalAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.FieldCount != 3)
        {
            throw WalCheckpointFailed();
        }

        var busy = reader.GetInt32(0);
        _ = reader.GetInt32(1);
        _ = reader.GetInt32(2);
        if (busy != 0 || await reader.ReadAsync(cancellationToken))
        {
            throw WalCheckpointFailed();
        }
    }

    private static InvalidOperationException WalCheckpointFailed()
    {
        return new("SQLite could not truncate the write-ahead log after credential cleanup.");
    }
}
