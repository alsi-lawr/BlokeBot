using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlokeBot.Persistence;

public enum MainDatabaseFailureKind
{
    UniqueConflict,
    SerializationFailure,
    Deadlock,
    LockTimeout,
    QueryTimeout,
    TransientConnection,
    CallerCancellation,
    Terminal,
}

public static class MainDatabaseFailureClassifier
{
    public static MainDatabaseFailureKind Classify(
        Exception exception,
        CancellationToken callerCancellation = default
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            _ when callerCancellation.IsCancellationRequested && IsCancellation(exception) =>
                MainDatabaseFailureKind.CallerCancellation,
            DbUpdateConcurrencyException => MainDatabaseFailureKind.SerializationFailure,
            DbUpdateException { InnerException: { } inner } => Classify(inner, callerCancellation),
            NpgsqlException
            {
                InnerException: TimeoutException or OperationCanceledException,
            } npgsql => Classify(npgsql.InnerException!, callerCancellation),
            _ => ClassifyProviderFailure(exception),
        };
    }

    private static bool IsCancellation(Exception exception) =>
        exception
            is OperationCanceledException
                or PostgresException { SqlState: PostgresErrorCodes.QueryCanceled }
                or NpgsqlException { InnerException: OperationCanceledException };

    private static MainDatabaseFailureKind ClassifyProviderFailure(Exception exception) =>
        exception switch
        {
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT,
                SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE
                    or SQLitePCL.raw.SQLITE_CONSTRAINT_PRIMARYKEY,
            } => MainDatabaseFailureKind.UniqueConflict,
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED,
            } => MainDatabaseFailureKind.LockTimeout,
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } =>
                MainDatabaseFailureKind.UniqueConflict,
            PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } =>
                MainDatabaseFailureKind.SerializationFailure,
            PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected } =>
                MainDatabaseFailureKind.Deadlock,
            PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable } =>
                MainDatabaseFailureKind.LockTimeout,
            PostgresException { SqlState: PostgresErrorCodes.QueryCanceled } =>
                MainDatabaseFailureKind.QueryTimeout,
            PostgresException postgres
                when postgres.SqlState.StartsWith("08", StringComparison.Ordinal) =>
                MainDatabaseFailureKind.TransientConnection,
            DbException { IsTransient: true } => MainDatabaseFailureKind.TransientConnection,
            TimeoutException => MainDatabaseFailureKind.QueryTimeout,
            OperationCanceledException => MainDatabaseFailureKind.QueryTimeout,
            _ => MainDatabaseFailureKind.Terminal,
        };

    public static bool IsRetryableTransactionContention(
        Exception exception,
        CancellationToken callerCancellation = default
    ) =>
        Classify(exception, callerCancellation)
            is MainDatabaseFailureKind.UniqueConflict
                or MainDatabaseFailureKind.SerializationFailure
                or MainDatabaseFailureKind.Deadlock
                or MainDatabaseFailureKind.LockTimeout;

    public static bool IsContention(
        Exception exception,
        CancellationToken callerCancellation = default
    ) =>
        Classify(exception, callerCancellation)
            is MainDatabaseFailureKind.SerializationFailure
                or MainDatabaseFailureKind.Deadlock
                or MainDatabaseFailureKind.LockTimeout;
}
