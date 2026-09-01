using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace BlokeBot.DatabaseWorkloads;

public sealed partial class SqliteBaselineRunner
{
    private async Task RunPairedAsync(
        WorkloadId id,
        WorkloadDefinition definition,
        IReadOnlyDictionary<WorkloadId, WorkloadMeasurements> measurements,
        bool measured,
        Func<int, int, CancellationToken, Task<ExecutionResult>> operation,
        CancellationToken cancellationToken
    )
    {
        var measure = measurements[id];
        var elapsed = Stopwatch.StartNew();
        var rounds = (int)
            Math.Ceiling(definition.Operations / (double)protocol.Concurrency.Writers);
        for (var index = 0; index < rounds; index++)
        {
            await RunRoundAsync(async worker =>
            {
                var logical = (index * protocol.Concurrency.Writers) + worker;
                if (logical >= definition.Operations)
                {
                    return;
                }
                var started = Stopwatch.GetTimestamp();
                var execution = await operation(index, worker, cancellationToken);
                if (measured)
                {
                    measure.Record(
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        execution.Outcome,
                        execution.BusyEvents,
                        execution.BusyWaitMilliseconds
                    );
                }
            });
        }
        if (measured)
        {
            await RecordCancellationAsync(measure);
            measure.AddElapsed(elapsed.Elapsed.TotalSeconds);
        }
    }

    private static async Task RunRoundAsync(Func<int, Task> operation)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () =>
        {
            await gate.Task;
            await operation(0);
        });
        var second = Task.Run(async () =>
        {
            await gate.Task;
            await operation(1);
        });
        gate.SetResult();
        await Task.WhenAll(first, second);
    }

    private async Task<ExecutionResult> ExecuteWithRetryAsync(
        string connectionString,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<OperationOutcome>> action,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        long busyEvents = 0;
        var busyWait = TimeSpan.Zero;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var connection = await OpenAsync(connectionString, cancellationToken);
                var admissionStarted = Stopwatch.GetTimestamp();
                await using var transaction = connection.BeginTransaction(deferred: false);
                var admissionWait = Stopwatch.GetElapsedTime(admissionStarted);
                if (admissionWait >= TimeSpan.FromMilliseconds(1))
                {
                    busyEvents++;
                    busyWait += admissionWait;
                }
                var outcome = await action(connection, transaction, cancellationToken);
                transaction.Commit();
                return new(outcome, busyEvents, busyWait.TotalMilliseconds);
            }
            catch (SqliteException exception)
                when (exception.SqliteErrorCode is 5 or 6
                    && attempt < protocol.Concurrency.MaxBusyRetries
                )
            {
                busyEvents++;
                var waitStarted = Stopwatch.GetTimestamp();
                await Task.Delay(
                    protocol.Concurrency.BusyRetryDelayMilliseconds,
                    cancellationToken
                );
                busyWait += Stopwatch.GetElapsedTime(waitStarted);
            }
        }
    }

    private static async Task<SqliteConnection> OpenAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 0;";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task ConfigureDatabaseAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA wal_autocheckpoint = 0;";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            _ = command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> ScalarLongAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<(Guid Id, long Revision)?> ReadPairAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetInt64(1))
            : null;
    }

    private static async Task<string> ScalarStringAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture
            ) ?? string.Empty;
    }

    private static async Task CheckpointAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ReadPluginRevisionAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = await OpenAsync(connectionString, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Revision FROM plugin_feature_states WHERE PluginId = 'synthetic-plugin' AND FeatureId = 'synthetic-feature' AND HostId = 1;";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    private async Task RecordCancellationAsync(WorkloadMeasurements measurements)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            _ = await ExecuteWithRetryAsync(
                string.Empty,
                static (_, _, _) => Task.FromResult(OperationOutcome.Committed),
                cancellation.Token
            );
            throw new InvalidDataException(
                "The frozen pre-admission cancellation was not observed."
            );
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            measurements.Record(0, OperationOutcome.Cancelled, 0, 0);
        }
    }

    private sealed record ExecutionResult(
        OperationOutcome Outcome,
        long BusyEvents,
        double BusyWaitMilliseconds
    );
}
