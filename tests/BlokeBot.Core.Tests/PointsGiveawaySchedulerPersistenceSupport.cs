using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public abstract partial class PointsGiveawaySchedulerTestBase
{
    private protected sealed class FailingOnceDbContextFactory(
        IDbContextFactory<BlokeBotDbContext> inner,
        Exception failure
    ) : IDbContextFactory<BlokeBotDbContext>
    {
        public int Attempts { get; private set; }

        public BlokeBotDbContext CreateDbContext()
        {
            if (++Attempts == 1)
            {
                throw failure;
            }

            return inner.CreateDbContext();
        }

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            if (++Attempts == 1)
            {
                return Task.FromException<BlokeBotDbContext>(failure);
            }

            return inner.CreateDbContextAsync(cancellationToken);
        }
    }

    private protected sealed class RecordingDbContextFactory(
        IDbContextFactory<BlokeBotDbContext> inner
    ) : IDbContextFactory<BlokeBotDbContext>
    {
        private readonly ConcurrentQueue<DbConnection> _connections = [];

        public DbConnection[] Connections => _connections.ToArray();

        public BlokeBotDbContext CreateDbContext()
        {
            var db = inner.CreateDbContext();
            _connections.Enqueue(db.Database.GetDbConnection());
            return db;
        }

        public async Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            var db = await inner.CreateDbContextAsync(cancellationToken);
            _connections.Enqueue(db.Database.GetDbConnection());
            return db;
        }
    }

    private protected sealed class InterceptedSqliteBlokeBotDbFactory(
        SqliteConnection keeperConnection,
        DbContextOptions<BlokeBotDbContext> options
    ) : IDbContextFactory<BlokeBotDbContext>, IAsyncDisposable
    {
        public static async Task<InterceptedSqliteBlokeBotDbFactory> CreateAsync(
            IInterceptor interceptor
        )
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"BlokeBotInterceptedTests-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 0,
            }.ToString();
            var keeperConnection = new SqliteConnection(connectionString);
            await keeperConnection.OpenAsync();
            var creationOptions = new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite(connectionString)
                .Options;
            await using (var db = new BlokeBotDbContext(creationOptions))
            {
                await db.Database.EnsureCreatedAsync();
            }

            var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(interceptor)
                .Options;
            return new InterceptedSqliteBlokeBotDbFactory(keeperConnection, options);
        }

        public BlokeBotDbContext CreateDbContext()
        {
            return new(options);
        }

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(CreateDbContext());
        }

        public async ValueTask DisposeAsync()
        {
            await keeperConnection.DisposeAsync();
        }
    }

    private protected sealed class CommitCancellationInterceptor : DbTransactionInterceptor
    {
        private bool _failNextCommit;

        public int CommitAttempts { get; private set; }

        public CancellationToken ObservedCancellationToken { get; private set; }

        public void FailNextCommit()
        {
            _failNextCommit = true;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (!_failNextCommit)
            {
                return ValueTask.FromResult(result);
            }

            _failNextCommit = false;
            CommitAttempts++;
            ObservedCancellationToken = cancellationToken;
            return ValueTask.FromException<InterceptionResult>(
                new OperationCanceledException("commit cancellation")
            );
        }
    }

    private protected sealed class TestDatabaseException : DbException;
}
