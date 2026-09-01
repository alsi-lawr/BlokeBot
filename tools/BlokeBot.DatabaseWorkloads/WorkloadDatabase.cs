using System.Data.Common;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.DatabaseWorkloads;

internal abstract class WorkloadDatabase : IAsyncDisposable
{
    internal abstract string Provider { get; }

    internal abstract Task PrepareRunAsync(int run, CancellationToken cancellationToken);

    internal abstract Task<DbConnection> OpenAsync(CancellationToken cancellationToken);

    internal abstract Task<DbTransaction> BeginWriteAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    );

    internal abstract void Configure(DbContextOptionsBuilder<BlokeBotDbContext> options);

    internal abstract bool IsRetryableContention(Exception exception);

    internal abstract string InsertIgnore(string sqlite, string postgreSql);

    internal abstract string CommandText(string sql);

    internal abstract string ParameterName(string name);

    internal abstract Task<string> ReadVersionAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    );

    internal abstract Task<StorageResult> ReadStorageAsync(CancellationToken cancellationToken);

    internal abstract string Explain(string sql);

    internal abstract string ReadPlanStep(DbDataReader reader);

    public abstract ValueTask DisposeAsync();
}
