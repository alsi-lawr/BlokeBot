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

    public DatabaseCutoverRunner() { }

    internal DatabaseCutoverRunner(Action<CutoverBatchCommit> batchCommitted)
    {
        ArgumentNullException.ThrowIfNull(batchCommitted);
        _batchCommitted = batchCommitted;
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

            var sourceConfiguration = BlokeBotDatabaseConfiguration.Sqlite(
                options.SqliteDatabasePath
            );
            var targetConfiguration = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
                options.PostgreSqlConnectionStringFile
            );
            await using var source = sourceConfiguration.CreateDbContext();
            await using var target = targetConfiguration.CreateDbContext();
            var sourceConnection = (SqliteConnection)source.Database.GetDbConnection();
            sourceConnection.ConnectionString = new SqliteConnectionStringBuilder(
                sourceConnection.ConnectionString
            )
            {
                Pooling = false,
            }.ToString();
            var targetConnection = (NpgsqlConnection)target.Database.GetDbConnection();
            await sourceConnection.OpenAsync(cancellationToken);
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
                var schema = await ValidateSchemaAsync(source, target, cancellationToken);
                if (schema.Failure is not null)
                {
                    return new DatabaseCutoverResult.Rejected(schema.Failure);
                }

                var tables = CutoverCatalog.Load(source, target);
                var catalogFailure = await ValidatePhysicalCatalogsAsync(
                    sourceConnection,
                    targetConnection,
                    tables,
                    cancellationToken
                );
                if (catalogFailure is not null)
                {
                    return new DatabaseCutoverResult.Rejected(catalogFailure);
                }

                await using var sourceLease = await SqliteExclusiveLease.AcquireAsync(
                    sourceConnection,
                    cancellationToken
                );
                var sourceFingerprint = await CutoverFingerprint.SourceAsync(
                    sourceConnection,
                    null,
                    schema.SourceMigrations,
                    tables,
                    cancellationToken
                );
                var targetFingerprint = await CutoverFingerprint.TargetIdentityAsync(
                    targetConnection,
                    schema.TargetMigrations,
                    tables,
                    cancellationToken
                );
                var localStateFingerprint = await LocalStateFingerprint.CalculateAsync(
                    options.StateDirectory,
                    options.SqliteDatabasePath,
                    receiptStore,
                    cancellationToken
                );

                var receiptResult = await BindReceiptAsync(
                    receiptStore,
                    receipt,
                    options.OperationId,
                    sourceFingerprint,
                    targetFingerprint,
                    localStateFingerprint,
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
                    schema.SourceMigrations,
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
        || string.IsNullOrWhiteSpace(options.PostgreSqlConnectionStringFile)
            ? "StateDirectory, the SQLite database path, and the PostgreSql connection-string file are required."
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

internal sealed record CutoverSchemaValidation(
    IReadOnlyList<string> SourceMigrations,
    IReadOnlyList<string> TargetMigrations,
    string? Failure
);

internal sealed record CutoverReceiptResult(CutoverReceipt? Receipt, string? Failure);
