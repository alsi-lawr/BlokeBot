using BlokeBot.Hosting;
using Npgsql;
using Shouldly;

namespace BlokeBot.Tests;

public sealed class BlokeBotDatabaseOperationalTests
{
    [Test]
    public void ProviderStartupFailures_ReportUnavailableAuthenticationOrMigration()
    {
        BlokeBotDatabaseOperationalClassifier
            .Classify(
                new InvalidOperationException("provider wrapper", PostgreSqlFailure("08006")),
                BlokeBotDatabaseFailurePhase.Migration
            )
            .ShouldBe(BlokeBotDatabaseHealthCategory.ProviderUnavailable);
        BlokeBotDatabaseOperationalClassifier
            .Classify(PostgreSqlFailure("28P01"), BlokeBotDatabaseFailurePhase.Migration)
            .ShouldBe(BlokeBotDatabaseHealthCategory.AuthenticationFailure);
        BlokeBotDatabaseOperationalClassifier
            .Classify(
                new InvalidOperationException("synthetic migration failure"),
                BlokeBotDatabaseFailurePhase.Migration
            )
            .ShouldBe(BlokeBotDatabaseHealthCategory.MigrationFailure);
    }

    [Test]
    public void PoolAcquisitionAndCommandTimeout_ReportDifferentCategories()
    {
        var timeout = new NpgsqlException("synthetic timeout", new TimeoutException());

        BlokeBotDatabaseOperationalClassifier
            .Classify(timeout, BlokeBotDatabaseFailurePhase.Connection)
            .ShouldBe(BlokeBotDatabaseHealthCategory.PoolExhaustion);
        BlokeBotDatabaseOperationalClassifier
            .Classify(timeout, BlokeBotDatabaseFailurePhase.Command)
            .ShouldBe(BlokeBotDatabaseHealthCategory.CommandTimeout);
    }

    [Test]
    public void ConcurrencyAndApplicationConflicts_ReportDifferentCategories()
    {
        BlokeBotDatabaseOperationalClassifier
            .Classify(
                PostgreSqlFailure(PostgresErrorCodes.SerializationFailure),
                BlokeBotDatabaseFailurePhase.Application
            )
            .ShouldBe(BlokeBotDatabaseHealthCategory.RetryableConcurrencyConflict);
        BlokeBotDatabaseOperationalClassifier
            .Classify(
                PostgreSqlFailure(PostgresErrorCodes.UniqueViolation),
                BlokeBotDatabaseFailurePhase.Application
            )
            .ShouldBe(BlokeBotDatabaseHealthCategory.TerminalApplicationConflict);
    }

    [Test]
    public async Task Startup_ProviderUnavailabilityRetriesOnlyWithinTheAttemptLimit()
    {
        var connectionAttempts = 0;
        var migrationAttempts = 0;
        var delays = 0;

        var exception = await Should.ThrowAsync<BlokeBotDatabaseStartupException>(() =>
            BlokeBotDatabaseStartup.ExecuteAsync(
                _ =>
                {
                    connectionAttempts++;
                    return Task.FromException(PostgreSqlFailure("08006"));
                },
                _ =>
                {
                    migrationAttempts++;
                    return Task.CompletedTask;
                },
                (duration, _) =>
                {
                    duration.ShouldBe(BlokeBotDatabaseStartup.RetryDelay);
                    delays++;
                    return Task.CompletedTask;
                },
                CancellationToken.None
            )
        );

        exception.Category.ShouldBe(BlokeBotDatabaseHealthCategory.ProviderUnavailable);
        connectionAttempts.ShouldBe(BlokeBotDatabaseStartup.AttemptLimit);
        migrationAttempts.ShouldBe(0);
        delays.ShouldBe(BlokeBotDatabaseStartup.AttemptLimit - 1);
    }

    [Test]
    public async Task Startup_TerminalFailureDoesNotRetryAndKeepsSecretOutOfSummary()
    {
        const string Secret = "password-value-that-must-not-escape";
        var connectionAttempts = 0;
        var migrationAttempts = 0;
        var delays = 0;

        var exception = await Should.ThrowAsync<BlokeBotDatabaseStartupException>(() =>
            BlokeBotDatabaseStartup.ExecuteAsync(
                _ =>
                {
                    connectionAttempts++;
                    return Task.FromException(
                        new PostgresException(Secret, "ERROR", "ERROR", "28P01")
                    );
                },
                _ =>
                {
                    migrationAttempts++;
                    return Task.CompletedTask;
                },
                (_, _) =>
                {
                    delays++;
                    return Task.CompletedTask;
                },
                CancellationToken.None
            )
        );

        exception.Category.ShouldBe(BlokeBotDatabaseHealthCategory.AuthenticationFailure);
        exception.Summary.ShouldBe("blokebot: database startup failed (authentication-failure).");
        exception.Summary.ShouldNotContain(Secret);
        connectionAttempts.ShouldBe(1);
        migrationAttempts.ShouldBe(0);
        delays.ShouldBe(0);
    }

    [Test]
    public async Task Startup_MigrationFailureIsTerminalAfterTheConnectionCheck()
    {
        var connectionAttempts = 0;
        var migrationAttempts = 0;
        var delays = 0;

        var exception = await Should.ThrowAsync<BlokeBotDatabaseStartupException>(() =>
            BlokeBotDatabaseStartup.ExecuteAsync(
                _ =>
                {
                    connectionAttempts++;
                    return Task.CompletedTask;
                },
                _ =>
                {
                    migrationAttempts++;
                    return Task.FromException(
                        new InvalidOperationException("synthetic schema failure")
                    );
                },
                (_, _) =>
                {
                    delays++;
                    return Task.CompletedTask;
                },
                CancellationToken.None
            )
        );

        exception.Category.ShouldBe(BlokeBotDatabaseHealthCategory.MigrationFailure);
        connectionAttempts.ShouldBe(1);
        migrationAttempts.ShouldBe(1);
        delays.ShouldBe(0);
    }

    private static PostgresException PostgreSqlFailure(string sqlState) =>
        new("synthetic", "ERROR", "ERROR", sqlState);
}
