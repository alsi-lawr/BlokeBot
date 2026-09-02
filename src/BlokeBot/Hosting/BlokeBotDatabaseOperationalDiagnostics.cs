using System.Diagnostics;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlokeBot.Hosting;

internal enum BlokeBotDatabaseHealthCategory
{
    Ready,
    ProviderUnavailable,
    AuthenticationFailure,
    MigrationFailure,
    PoolExhaustion,
    CommandTimeout,
    RetryableConcurrencyConflict,
    TerminalApplicationConflict,
}

internal enum BlokeBotDatabaseFailurePhase
{
    Connection,
    Command,
    Migration,
    Application,
}

internal static class BlokeBotDatabaseHealthCategoryExtensions
{
    internal static string Token(this BlokeBotDatabaseHealthCategory category) =>
        category switch
        {
            BlokeBotDatabaseHealthCategory.Ready => "ready",
            BlokeBotDatabaseHealthCategory.ProviderUnavailable => "provider-unavailable",
            BlokeBotDatabaseHealthCategory.AuthenticationFailure => "authentication-failure",
            BlokeBotDatabaseHealthCategory.MigrationFailure => "migration-failure",
            BlokeBotDatabaseHealthCategory.PoolExhaustion => "pool-exhaustion",
            BlokeBotDatabaseHealthCategory.CommandTimeout => "command-timeout",
            BlokeBotDatabaseHealthCategory.RetryableConcurrencyConflict =>
                "retryable-concurrency-conflict",
            BlokeBotDatabaseHealthCategory.TerminalApplicationConflict =>
                "terminal-application-conflict",
        };
}

internal static class BlokeBotDatabaseOperationalClassifier
{
    internal static BlokeBotDatabaseHealthCategory Classify(
        Exception exception,
        BlokeBotDatabaseFailurePhase phase
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        var providerFailure = ProviderFailure(exception);
        return ProviderCategory(providerFailure, phase)
            ?? ClassifyMainDatabaseFailure(providerFailure, phase);
    }

    private static BlokeBotDatabaseHealthCategory? ProviderCategory(
        Exception providerFailure,
        BlokeBotDatabaseFailurePhase phase
    ) =>
        (phase, providerFailure) switch
        {
            (_, PostgresException postgres)
                when postgres.SqlState.StartsWith("28", StringComparison.Ordinal) =>
                BlokeBotDatabaseHealthCategory.AuthenticationFailure,
            (
                BlokeBotDatabaseFailurePhase.Connection,
                NpgsqlException { InnerException: TimeoutException }
            ) => BlokeBotDatabaseHealthCategory.PoolExhaustion,
            _ => null,
        };

    private static BlokeBotDatabaseHealthCategory ClassifyMainDatabaseFailure(
        Exception exception,
        BlokeBotDatabaseFailurePhase phase
    ) =>
        (phase, MainDatabaseFailureClassifier.Classify(exception)) switch
        {
            (_, MainDatabaseFailureKind.TransientConnection) =>
                BlokeBotDatabaseHealthCategory.ProviderUnavailable,
            (BlokeBotDatabaseFailurePhase.Connection, MainDatabaseFailureKind.QueryTimeout) =>
                BlokeBotDatabaseHealthCategory.ProviderUnavailable,
            (_, MainDatabaseFailureKind.QueryTimeout) =>
                BlokeBotDatabaseHealthCategory.CommandTimeout,
            (
                _,
                MainDatabaseFailureKind.SerializationFailure
                    or MainDatabaseFailureKind.Deadlock
                    or MainDatabaseFailureKind.LockTimeout
            ) => BlokeBotDatabaseHealthCategory.RetryableConcurrencyConflict,
            (_, MainDatabaseFailureKind.UniqueConflict) =>
                BlokeBotDatabaseHealthCategory.TerminalApplicationConflict,
            (BlokeBotDatabaseFailurePhase.Migration, _) =>
                BlokeBotDatabaseHealthCategory.MigrationFailure,
            (_, MainDatabaseFailureKind.CallerCancellation or MainDatabaseFailureKind.Terminal) =>
                BlokeBotDatabaseHealthCategory.TerminalApplicationConflict,
        };

    private static Exception ProviderFailure(Exception exception) =>
        exception switch
        {
            DbUpdateException { InnerException: { } inner } => ProviderFailure(inner),
            InvalidOperationException { InnerException: { } inner } => ProviderFailure(inner),
            _ => exception,
        };
}

internal sealed record BlokeBotDatabaseHealthResult(
    BlokeBotDatabaseHealthCategory Category,
    BlokeBotDatabaseProvider Provider
)
{
    internal bool IsReady => Category == BlokeBotDatabaseHealthCategory.Ready;
}

internal sealed class BlokeBotDatabaseHealthProbe(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BlokeBotDatabaseConfiguration configuration
)
{
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    internal async Task<BlokeBotDatabaseHealthResult> ProbeAsync(
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        var phase = BlokeBotDatabaseFailurePhase.Connection;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(timeout.Token);
            await db.Database.OpenConnectionAsync(timeout.Token);
            phase = BlokeBotDatabaseFailurePhase.Command;
            var pending = await db.Database.GetPendingMigrationsAsync(timeout.Token);
            return pending.Any()
                ? Result(BlokeBotDatabaseHealthCategory.MigrationFailure)
                : Result(BlokeBotDatabaseHealthCategory.Ready);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(BlokeBotDatabaseOperationalClassifier.Classify(exception, phase));
        }
    }

    private BlokeBotDatabaseHealthResult Result(BlokeBotDatabaseHealthCategory category) =>
        new(category, configuration.Provider);
}

internal sealed class BlokeBotDatabaseStartupException(
    BlokeBotDatabaseHealthCategory category,
    Exception innerException
) : Exception("BlokeBot database startup failed.", innerException)
{
    internal BlokeBotDatabaseHealthCategory Category { get; } = category;

    internal string Summary => $"blokebot: database startup failed ({Category.Token()}).";
}

internal static class BlokeBotDatabaseStartup
{
    internal const int AttemptLimit = 5;
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    internal static Task InitializeAsync(WebApplication app, CancellationToken cancellationToken) =>
        ExecuteAsync(
            async attemptCancellation =>
            {
                var factory = app.Services.GetRequiredService<
                    IDbContextFactory<BlokeBotDbContext>
                >();
                await using var db = await factory.CreateDbContextAsync(attemptCancellation);
                await db.Database.OpenConnectionAsync(attemptCancellation);
            },
            app.InitializeBlokeBotPersistenceAsync,
            Task.Delay,
            cancellationToken
        );

    internal static async Task ExecuteAsync(
        Func<CancellationToken, Task> verifyConnection,
        Func<CancellationToken, Task> migrate,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(verifyConnection);
        ArgumentNullException.ThrowIfNull(migrate);
        ArgumentNullException.ThrowIfNull(delay);

        for (var attempt = 1; attempt <= AttemptLimit; attempt++)
        {
            var outcome = await AttemptAsync(verifyConnection, migrate, cancellationToken);
            if (outcome is BlokeBotDatabaseStartupAttempt.Succeeded)
            {
                return;
            }

            var failure = (BlokeBotDatabaseStartupAttempt.Failed)outcome;
            if (ShouldRetry(failure.Category, attempt))
            {
                await delay(RetryDelay, cancellationToken);
                continue;
            }

            throw new BlokeBotDatabaseStartupException(failure.Category, failure.Exception);
        }

        throw new UnreachableException();
    }

    private static async Task<BlokeBotDatabaseStartupAttempt> AttemptAsync(
        Func<CancellationToken, Task> verifyConnection,
        Func<CancellationToken, Task> migrate,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await verifyConnection(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(exception, BlokeBotDatabaseFailurePhase.Connection);
        }

        try
        {
            await migrate(cancellationToken);
            return new BlokeBotDatabaseStartupAttempt.Succeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(exception, BlokeBotDatabaseFailurePhase.Migration);
        }
    }

    private static BlokeBotDatabaseStartupAttempt.Failed Failure(
        Exception exception,
        BlokeBotDatabaseFailurePhase phase
    ) => new(BlokeBotDatabaseOperationalClassifier.Classify(exception, phase), exception);

    private static bool ShouldRetry(BlokeBotDatabaseHealthCategory category, int attempt) =>
        category == BlokeBotDatabaseHealthCategory.ProviderUnavailable && attempt < AttemptLimit;
}

internal abstract record BlokeBotDatabaseStartupAttempt
{
    private BlokeBotDatabaseStartupAttempt() { }

    internal sealed record Succeeded : BlokeBotDatabaseStartupAttempt;

    internal sealed record Failed(BlokeBotDatabaseHealthCategory Category, Exception Exception)
        : BlokeBotDatabaseStartupAttempt;
}
