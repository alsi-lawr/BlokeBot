using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace BlokeBot.Tests;

public sealed class MainDatabaseFailureClassifierTests
{
    [Test]
    [Arguments(PostgresErrorCodes.UniqueViolation, MainDatabaseFailureKind.UniqueConflict)]
    [Arguments(
        PostgresErrorCodes.SerializationFailure,
        MainDatabaseFailureKind.SerializationFailure
    )]
    [Arguments(PostgresErrorCodes.DeadlockDetected, MainDatabaseFailureKind.Deadlock)]
    [Arguments(PostgresErrorCodes.LockNotAvailable, MainDatabaseFailureKind.LockTimeout)]
    [Arguments(PostgresErrorCodes.QueryCanceled, MainDatabaseFailureKind.QueryTimeout)]
    [Arguments("08006", MainDatabaseFailureKind.TransientConnection)]
    public void PostgreSqlFailure_IsDistinguished(
        string sqlState,
        MainDatabaseFailureKind expected
    ) => MainDatabaseFailureClassifier.Classify(PostgreSqlFailure(sqlState)).ShouldBe(expected);

    [Test]
    public void ProviderFailure_WrappedByEntityFramework_RetainsItsClassification()
    {
        var failure = new DbUpdateException(
            "The update failed.",
            PostgreSqlFailure(PostgresErrorCodes.SerializationFailure)
        );

        MainDatabaseFailureClassifier
            .Classify(failure)
            .ShouldBe(MainDatabaseFailureKind.SerializationFailure);
    }

    [Test]
    public void OptimisticConcurrencyFailure_IsNotReclassifiedByItsInnerFailure() =>
        MainDatabaseFailureClassifier
            .Classify(
                new DbUpdateConcurrencyException(
                    "The expected revision changed.",
                    new InvalidOperationException("provider detail")
                )
            )
            .ShouldBe(MainDatabaseFailureKind.SerializationFailure);

    [Test]
    public void CallerCancellation_IsNotClassifiedAsProviderTimeout()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        MainDatabaseFailureClassifier
            .Classify(new OperationCanceledException(caller.Token), caller.Token)
            .ShouldBe(MainDatabaseFailureKind.CallerCancellation);
        MainDatabaseFailureClassifier
            .Classify(PostgreSqlFailure(PostgresErrorCodes.QueryCanceled), caller.Token)
            .ShouldBe(MainDatabaseFailureKind.CallerCancellation);
        MainDatabaseFailureClassifier
            .Classify(new OperationCanceledException())
            .ShouldBe(MainDatabaseFailureKind.QueryTimeout);
    }

    [Test]
    public void PostgreSqlCommandTimeoutWrapper_IsNotAConnectionFailure() =>
        MainDatabaseFailureClassifier
            .Classify(new NpgsqlException("Command timed out.", new TimeoutException()))
            .ShouldBe(MainDatabaseFailureKind.QueryTimeout);

    [Test]
    public void TerminalFailure_IsNotRetryableContention() =>
        MainDatabaseFailureClassifier
            .IsRetryableTransactionContention(new InvalidOperationException("terminal"))
            .ShouldBeFalse();

    private static PostgresException PostgreSqlFailure(string sqlState) =>
        new("synthetic", "ERROR", "ERROR", sqlState);
}
