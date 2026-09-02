using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private const string _currentSqliteMigration = "20260826174307_v0.13.0";
    private const string _currentPostgreSqlMigration = "20260901145930_20260901_v0_14_0_Baseline";
    private readonly Action<CutoverBatchCommit>? _batchCommitted;
    private readonly Action<CutoverPreparationCheckpoint>? _preparationCheckpoint;

    public DatabaseCutoverRunner() { }

    internal DatabaseCutoverRunner(
        Action<CutoverBatchCommit>? batchCommitted,
        Action<CutoverPreparationCheckpoint>? preparationCheckpoint
    )
    {
        _batchCommitted = batchCommitted;
        _preparationCheckpoint = preparationCheckpoint;
    }

    public async Task<DatabaseCutoverResult> RunAsync(
        DatabaseCutoverOptions options,
        CancellationToken cancellationToken
    )
    {
        var optionFailure = ValidateOptions(options);
        if (optionFailure is not null)
        {
            return new DatabaseCutoverResult.Rejected(optionFailure);
        }

        CutoverReceiptStore? receiptStore = null;
        CutoverReceipt? receipt = null;
        try
        {
            receiptStore = new CutoverReceiptStore(options.StateDirectory);
            receipt = await receiptStore.ReadAsync(cancellationToken);
            var administratorConfiguration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
                options.PostgreSqlAdministratorConnectionStringFile
            );
            var applicationConfiguration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
                options.PostgreSqlApplicationConnectionStringFile
            );
            var sourceConfiguration = BlokeBotDatabaseConfiguration.Sqlite(
                options.SqliteDatabasePath
            );
            await InitializeAsync(sourceConfiguration, cancellationToken);

            await using var source = CutoverDbContextFactory.CreateDbContext(sourceConfiguration);
            await using var target = CutoverDbContextFactory.CreateDbContext(
                applicationConfiguration
            );
            var sourceConnection = (SqliteConnection)source.Database.GetDbConnection();
            await sourceConnection.OpenAsync(cancellationToken);
            await using var sourceLease = await SqliteExclusiveLease.AcquireAsync(
                sourceConnection,
                cancellationToken
            );
            var sourceMigrations = await ReadMigrationHistoryAsync(
                source,
                _currentSqliteMigration,
                cancellationToken
            );
            if (!sourceMigrations.IsCurrent)
            {
                return new DatabaseCutoverResult.Rejected(
                    "The SQLite source did not reach the current supported schema."
                );
            }

            var tables = CutoverCatalog.Load(source, target);
            var sourceCatalogFailure = await ValidateSqlitePhysicalCatalogAsync(
                sourceConnection,
                tables,
                cancellationToken
            );
            if (sourceCatalogFailure is not null)
            {
                return new DatabaseCutoverResult.Rejected(sourceCatalogFailure);
            }

            var sourceFingerprint = await CutoverFingerprint.SourceAsync(
                sourceConnection,
                null,
                sourceMigrations.Applied,
                tables,
                cancellationToken
            );
            var localStateFingerprint = await LocalStateFingerprint.CalculateAsync(
                options.StateDirectory,
                options.SqliteDatabasePath,
                receiptStore,
                cancellationToken
            );
            var targetConnection = (NpgsqlConnection)target.Database.GetDbConnection();
            var preparation = await PrepareTargetAsync(
                administratorConfiguration,
                applicationConfiguration,
                targetConnection,
                options.OperationId,
                receiptStore,
                receipt,
                sourceFingerprint,
                localStateFingerprint,
                cancellationToken
            );
            if (preparation.Failure is not null)
            {
                return new DatabaseCutoverResult.Rejected(preparation.Failure);
            }

            receipt = preparation.Receipt!;
            await targetConnection.OpenAsync(cancellationToken);
            var targetOwnershipFailure = await AcquireTargetOwnershipAsync(
                targetConnection,
                cancellationToken
            );
            if (targetOwnershipFailure is not null)
            {
                return new DatabaseCutoverResult.Rejected(targetOwnershipFailure);
            }

            try
            {
                var targetMigrations = await ReadMigrationHistoryAsync(
                    target,
                    _currentPostgreSqlMigration,
                    cancellationToken
                );
                if (!targetMigrations.IsCurrent)
                {
                    return new DatabaseCutoverResult.Rejected(
                        "The PostgreSql target did not reach the current compatible schema."
                    );
                }

                var catalogFailure = await ValidatePostgreSqlPhysicalCatalogAsync(
                    targetConnection,
                    tables,
                    cancellationToken
                );
                if (catalogFailure is not null)
                {
                    return new DatabaseCutoverResult.Rejected(catalogFailure);
                }

                var targetFingerprint = await CutoverFingerprint.TargetIdentityAsync(
                    targetConnection,
                    receipt.PostgreSqlClusterIdentity,
                    targetMigrations.Applied,
                    tables,
                    cancellationToken
                );
                var receiptResult = await BindReceiptAsync(
                    receiptStore,
                    receipt,
                    targetFingerprint,
                    targetConnection,
                    tables,
                    cancellationToken
                );
                if (receiptResult.Failure is not null)
                {
                    return new DatabaseCutoverResult.Rejected(receiptResult.Failure);
                }

                receipt = receiptResult.Receipt!;
                if (receipt.Phase == CutoverPhase.Complete)
                {
                    return new DatabaseCutoverResult.Succeeded(
                        receipt.OperationId,
                        receiptStore.Path,
                        AlreadyComplete: true
                    );
                }

                Checkpoint(CutoverPreparationCheckpoint.TargetBound, cancellationToken);
                var reconcile = await ReconcileTargetAsync(
                    receipt,
                    receiptStore,
                    sourceConnection,
                    null,
                    targetConnection,
                    tables,
                    options.BatchSize,
                    cancellationToken
                );
                if (reconcile.Failure is not null)
                {
                    await RecordFailureAsync(receiptStore, receipt, "target-reconciliation-failed");
                    return new DatabaseCutoverResult.Rejected(reconcile.Failure);
                }

                receipt = reconcile.Receipt!;
                receipt = await CopyAsync(
                    receipt,
                    receiptStore,
                    sourceConnection,
                    null,
                    targetConnection,
                    tables,
                    options.BatchSize,
                    cancellationToken
                );
                receipt = await AdvanceSequencesAsync(
                    receipt,
                    receiptStore,
                    targetConnection,
                    tables,
                    cancellationToken
                );
                var verification = await VerifyAsync(
                    receipt,
                    receiptStore,
                    sourceConnection,
                    null,
                    targetConnection,
                    tables,
                    sourceFingerprint,
                    localStateFingerprint,
                    options,
                    sourceMigrations.Applied,
                    cancellationToken
                );
                if (verification.Failure is not null)
                {
                    await RecordFailureAsync(receiptStore, receipt, "verification-failed");
                    return new DatabaseCutoverResult.Failed(verification.Failure);
                }

                receipt = verification.Receipt!.Completed();
                await receiptStore.WriteAsync(receipt, cancellationToken);
                return new DatabaseCutoverResult.Succeeded(
                    receipt.OperationId,
                    receiptStore.Path,
                    AlreadyComplete: false
                );
            }
            finally
            {
                await ReleaseTargetOwnershipAsync(targetConnection);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordFailureAsync(receiptStore, receipt, "cancelled");
            return new DatabaseCutoverResult.Failed(
                "The database cutover was cancelled. Run the same operation again to resume."
            );
        }
        catch (Exception exception) when (exception is BlokeBotDatabaseConfigurationException)
        {
            await RecordFailureAsync(receiptStore, receipt, "configuration");
            return new DatabaseCutoverResult.Rejected(exception.Message);
        }
        catch (Exception)
        {
            await RecordFailureAsync(receiptStore, receipt, "unexpected-failure");
            return new DatabaseCutoverResult.Failed(
                "The database cutover failed. The external receipt can resume the same operation."
            );
        }
    }

    private static string? ValidateOptions(DatabaseCutoverOptions options) =>
        string.IsNullOrWhiteSpace(options.StateDirectory)
        || string.IsNullOrWhiteSpace(options.SqliteDatabasePath)
        || string.IsNullOrWhiteSpace(options.PostgreSqlAdministratorConnectionStringFile)
        || string.IsNullOrWhiteSpace(options.PostgreSqlApplicationConnectionStringFile)
            ? "StateDirectory, the SQLite database path, and both PostgreSql connection-string files are required."
        : options.BatchSize is < 1 or > 5000
            ? "The database cutover batch size must be between 1 and 5000."
        : null;

    private static async Task RecordFailureAsync(
        CutoverReceiptStore? store,
        CutoverReceipt? receipt,
        string code
    )
    {
        if (store is not null)
        {
            var latest = await store.ReadAsync(CancellationToken.None) ?? receipt;
            if (latest is not null)
            {
                await store.WriteAsync(latest.Failed(code), CancellationToken.None);
            }
        }
    }
}

internal sealed record CutoverMigrationHistory(IReadOnlyList<string> Applied, bool IsCurrent);

internal sealed record CutoverReceiptResult(CutoverReceipt? Receipt, string? Failure);
