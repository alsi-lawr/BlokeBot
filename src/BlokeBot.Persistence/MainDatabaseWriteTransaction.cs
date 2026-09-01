using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlokeBot.Persistence;

public sealed class MainDatabaseWriteTransaction : IAsyncDisposable
{
    private const long _postgreSqlImmediateWriteLock = 0x426C6F6B65426F74;
    private readonly IAsyncDisposable? _providerTransaction;
    private readonly IDbContextTransaction _contextTransaction;

    private MainDatabaseWriteTransaction(
        IDbContextTransaction contextTransaction,
        IAsyncDisposable? providerTransaction = null
    )
    {
        _contextTransaction = contextTransaction;
        _providerTransaction = providerTransaction;
    }

    public static Task<MainDatabaseWriteTransaction> StartImmediateAsync(
        BlokeBotDbContext db,
        CancellationToken cancellationToken
    ) => StartAsync(db, null, cancellationToken);

    public static Task<MainDatabaseWriteTransaction> StartImmediateWithBoundedAdmissionAsync(
        BlokeBotDbContext db,
        TimeSpan admissionTimeout,
        CancellationToken cancellationToken
    ) =>
        admissionTimeout <= TimeSpan.Zero || admissionTimeout > TimeSpan.FromMinutes(1)
            ? throw new ArgumentOutOfRangeException(nameof(admissionTimeout))
            : StartAsync(db, admissionTimeout, cancellationToken);

    private static async Task<MainDatabaseWriteTransaction> StartAsync(
        BlokeBotDbContext db,
        TimeSpan? admissionTimeout,
        CancellationToken cancellationToken
    ) =>
        db.Database.Provider() switch
        {
            BlokeBotDatabaseProvider.Sqlite => await StartSqliteAsync(
                db,
                admissionTimeout,
                cancellationToken
            ),
            BlokeBotDatabaseProvider.PostgreSql => await StartPostgreSqlAsync(
                db,
                admissionTimeout,
                cancellationToken
            ),
        };

    private static async Task<MainDatabaseWriteTransaction> StartSqliteAsync(
        BlokeBotDbContext db,
        TimeSpan? admissionTimeout,
        CancellationToken cancellationToken
    )
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        cancellationToken.ThrowIfCancellationRequested();
        var defaultTimeout = connection.DefaultTimeout;
        SqliteTransaction providerTransaction;
        try
        {
            if (admissionTimeout is { } timeout)
            {
                connection.DefaultTimeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
            }
            providerTransaction = connection.BeginTransaction(deferred: false);
        }
        finally
        {
            connection.DefaultTimeout = defaultTimeout;
        }

        try
        {
            var contextTransaction =
                await db.Database.UseTransactionAsync(providerTransaction, cancellationToken)
                ?? throw new InvalidOperationException(
                    "The immediate main-database transaction could not be attached."
                );
            return new(contextTransaction, providerTransaction);
        }
        catch
        {
            await providerTransaction.DisposeAsync();
            throw;
        }
    }

    private static async Task<MainDatabaseWriteTransaction> StartPostgreSqlAsync(
        BlokeBotDbContext db,
        TimeSpan? admissionTimeout,
        CancellationToken cancellationToken
    )
    {
        var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        try
        {
            if (admissionTimeout is { } timeout)
            {
                var milliseconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
                _ = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('lock_timeout', {milliseconds.ToString(CultureInfo.InvariantCulture)} || 'ms', true);",
                    cancellationToken
                );
            }
            _ = await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({_postgreSqlImmediateWriteLock});",
                cancellationToken
            );
            return new(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public Task CommitAsync(CancellationToken cancellationToken) =>
        _contextTransaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _contextTransaction.DisposeAsync();
        if (_providerTransaction is not null)
        {
            await _providerTransaction.DisposeAsync();
        }
    }
}
