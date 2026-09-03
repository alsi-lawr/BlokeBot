using BlokeBot.Persistence;
using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private static async Task InitializeAsync(
        BlokeBotDatabaseConfiguration configuration,
        CancellationToken cancellationToken
    ) =>
        await new BlokeBotDatabaseInitializer(
            new CutoverDbContextFactory(configuration)
        ).InitializeAsync(cancellationToken);

    private async Task<CutoverReceiptResult> PrepareTargetAsync(
        NpgsqlConnection administrator,
        BlokeBotDatabaseConfiguration applicationConfiguration,
        NpgsqlConnection application,
        Guid? requestedOperationId,
        CutoverReceiptStore store,
        CutoverReceipt? existing,
        IReadOnlyList<CutoverTableRows> sourceRows,
        string localStateFingerprint,
        CancellationToken cancellationToken
    )
    {
        var applicationSettings = new NpgsqlConnectionStringBuilder(application.ConnectionString);
        var serverFailure = await ValidatePostgreSqlServerAsync(
            administrator,
            applicationSettings.Username!,
            cancellationToken
        );
        if (serverFailure is not null)
        {
            return new(null, serverFailure);
        }

        var target = new CutoverTargetIdentity(
            await ReadClusterIdentityAsync(administrator, cancellationToken),
            applicationSettings.Database!,
            applicationSettings.Username!
        );
        var databaseExists = await DatabaseExistsAsync(
            administrator,
            target.Database,
            cancellationToken
        );
        var bindingFailure = ValidatePreparationBinding(
            existing,
            requestedOperationId,
            sourceRows,
            localStateFingerprint,
            target,
            databaseExists
        );
        if (bindingFailure is not null)
        {
            return new(null, bindingFailure);
        }

        var receipt =
            existing
            ?? await WriteNewReceiptAsync(
                store,
                requestedOperationId,
                sourceRows,
                localStateFingerprint,
                target,
                cancellationToken
            );
        if (databaseExists)
        {
            var databaseFailure = await ValidateExistingDatabaseAsync(
                administrator,
                target,
                receipt.OperationId,
                cancellationToken
            );
            if (databaseFailure is not null)
            {
                return new(null, databaseFailure);
            }
        }
        else
        {
            await CreateDatabaseAsync(
                administrator,
                target,
                receipt.OperationId,
                cancellationToken
            );
        }

        if (receipt.Phase == CutoverPhase.DatabasePlanned)
        {
            Checkpoint(CutoverPreparationCheckpoint.DatabaseCreated, cancellationToken);
            receipt = await WritePhaseAsync(
                store,
                receipt,
                CutoverPhase.DatabaseCreated,
                cancellationToken
            );
        }

        var applicationFailure = await ValidateApplicationTargetAsync(
            application,
            target,
            receipt.OperationId,
            cancellationToken
        );
        if (applicationFailure is not null)
        {
            return new(null, applicationFailure);
        }

        if (receipt.Phase == CutoverPhase.DatabaseCreated)
        {
            await InitializeAsync(applicationConfiguration, cancellationToken);
            Checkpoint(CutoverPreparationCheckpoint.SchemaApplied, cancellationToken);
            receipt = await WritePhaseAsync(
                store,
                receipt,
                CutoverPhase.SchemaReady,
                cancellationToken
            );
        }

        return new(receipt, null);
    }

    private void Checkpoint(
        CutoverPreparationCheckpoint checkpoint,
        CancellationToken cancellationToken
    )
    {
        _preparationCheckpoint?.Invoke(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task<CutoverReceipt> WritePhaseAsync(
        CutoverReceiptStore store,
        CutoverReceipt receipt,
        CutoverPhase phase,
        CancellationToken cancellationToken
    )
    {
        var advanced = receipt.WithPhase(phase);
        await store.WriteAsync(advanced, cancellationToken);
        return advanced;
    }

    private async Task<CutoverReceipt> WriteNewReceiptAsync(
        CutoverReceiptStore store,
        Guid? requestedOperationId,
        IReadOnlyList<CutoverTableRows> sourceRows,
        string localStateFingerprint,
        CutoverTargetIdentity target,
        CancellationToken cancellationToken
    )
    {
        Checkpoint(CutoverPreparationCheckpoint.BeforeReceipt, cancellationToken);
        var receipt = new CutoverReceipt(
            CutoverReceipt.CurrentFormatVersion,
            requestedOperationId ?? Guid.NewGuid(),
            CutoverPhase.DatabasePlanned,
            _currentSqliteMigration,
            sourceRows,
            localStateFingerprint,
            target.ClusterIdentity,
            target.Database,
            target.Owner,
            [],
            null,
            null,
            DateTimeOffset.UtcNow,
            null
        );
        await store.WriteAsync(receipt, cancellationToken);
        Checkpoint(CutoverPreparationCheckpoint.ReceiptWritten, cancellationToken);
        return receipt;
    }

    private static string? ValidatePreparationBinding(
        CutoverReceipt? receipt,
        Guid? requestedOperationId,
        IReadOnlyList<CutoverTableRows> sourceRows,
        string localStateFingerprint,
        CutoverTargetIdentity target,
        bool databaseExists
    )
    {
        if (receipt is null)
        {
            return databaseExists
                ? "The PostgreSql database already exists without a matching external cutover receipt."
                : null;
        }

        if (requestedOperationId is { } requested && requested != receipt.OperationId)
        {
            return "The requested operation ID does not match the external cutover receipt.";
        }

        var matches =
            StringComparer.Ordinal.Equals(receipt.SqliteMigration, _currentSqliteMigration)
            && receipt.SourceRows.SequenceEqual(sourceRows)
            && StringComparer.Ordinal.Equals(receipt.LocalStateFingerprint, localStateFingerprint)
            && StringComparer.Ordinal.Equals(
                receipt.PostgreSqlClusterIdentity,
                target.ClusterIdentity
            )
            && StringComparer.Ordinal.Equals(receipt.PostgreSqlDatabase, target.Database)
            && StringComparer.Ordinal.Equals(receipt.PostgreSqlOwner, target.Owner);
        if (!matches)
        {
            return "The source, target, or local state does not match the external cutover receipt.";
        }

        // Only a receipt that never reached database creation may create the database.
        return databaseExists || receipt.Phase == CutoverPhase.DatabasePlanned
            ? null
            : "The PostgreSql database from the external cutover receipt does not exist.";
    }

    private static async Task<string?> ValidatePostgreSqlServerAsync(
        NpgsqlConnection administrator,
        string applicationRole,
        CancellationToken cancellationToken
    )
    {
        await using var command = administrator.CreateCommand();
        command.CommandText =
            "SELECT current_setting('server_version_num')::integer, EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role AND rolcanlogin);";
        _ = command.Parameters.AddWithValue("role", applicationRole);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        _ = await reader.ReadAsync(cancellationToken);
        return reader.GetInt32(0) is < 180000 or >= 190000
                ? "The administrator connection must target PostgreSQL 18.x."
            : reader.GetBoolean(1) ? null
            : "The PostgreSql application login does not exist or cannot log in.";
    }

    private static async Task<string> ReadClusterIdentityAsync(
        NpgsqlConnection administrator,
        CancellationToken cancellationToken
    )
    {
        await using var command = administrator.CreateCommand();
        command.CommandText = "SELECT system_identifier::text FROM pg_control_system();";
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> DatabaseExistsAsync(
        NpgsqlConnection administrator,
        string database,
        CancellationToken cancellationToken
    )
    {
        await using var command = administrator.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database);";
        _ = command.Parameters.AddWithValue("database", database);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    // The marker is written straight after creation so that a retry can tell this operation's
    // database from one that an operator created by hand.
    private static async Task CreateDatabaseAsync(
        NpgsqlConnection administrator,
        CutoverTargetIdentity target,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await using var create = administrator.CreateCommand();
        create.CommandText =
            $"CREATE DATABASE {QuoteIdentifier(target.Database)} OWNER {QuoteIdentifier(target.Owner)};";
        _ = await create.ExecuteNonQueryAsync(cancellationToken);

        // COMMENT is a utility statement and takes no bind parameters; the marker is a fixed
        // prefix plus a D-format GUID, so the literal cannot contain a quote.
        await using var mark = administrator.CreateCommand();
        mark.CommandText =
            $"COMMENT ON DATABASE {QuoteIdentifier(target.Database)} IS '{Marker(operationId)}';";
        _ = await mark.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> ValidateExistingDatabaseAsync(
        NpgsqlConnection administrator,
        CutoverTargetIdentity target,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await using var command = administrator.CreateCommand();
        command.CommandText =
            "SELECT role.rolname, shobj_description(database.oid, 'pg_database') FROM pg_database AS database JOIN pg_roles AS role ON role.oid = database.datdba WHERE database.datname = @database;";
        _ = command.Parameters.AddWithValue("database", target.Database);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        _ = await reader.ReadAsync(cancellationToken);
        var owner = reader.GetString(0);
        var marker = reader.IsDBNull(1) ? null : reader.GetString(1);
        return !StringComparer.Ordinal.Equals(owner, target.Owner)
                ? "The PostgreSql database owner does not match the external cutover receipt."
            : !StringComparer.Ordinal.Equals(marker, Marker(operationId))
                ? "The PostgreSql database is not bound to the external cutover receipt."
            : null;
    }

    private static async Task<string?> ValidateApplicationTargetAsync(
        NpgsqlConnection application,
        CutoverTargetIdentity target,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await application.OpenAsync(cancellationToken);
        try
        {
            await using var command = application.CreateCommand();
            command.CommandText =
                "SELECT current_database(), current_user, shobj_description(oid, 'pg_database') FROM pg_database WHERE datname = current_database();";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            _ = await reader.ReadAsync(cancellationToken);
            var matches =
                StringComparer.Ordinal.Equals(reader.GetString(0), target.Database)
                && StringComparer.Ordinal.Equals(reader.GetString(1), target.Owner)
                && !reader.IsDBNull(2)
                && StringComparer.Ordinal.Equals(reader.GetString(2), Marker(operationId));
            return matches
                ? null
                : "The PostgreSql application connection does not reach the prepared database.";
        }
        finally
        {
            await application.CloseAsync();
        }
    }

    private static string Marker(Guid operationId) => $"blokebot-cutover:{operationId:D}";

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

internal sealed record CutoverTargetIdentity(string ClusterIdentity, string Database, string Owner);

internal enum CutoverPreparationCheckpoint
{
    BeforeReceipt,
    ReceiptWritten,
    DatabaseCreated,
    SchemaApplied,
    TargetBound,
}
