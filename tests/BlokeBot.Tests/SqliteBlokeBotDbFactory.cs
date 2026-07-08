using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Tests;

internal sealed class SqliteBlokeBotDbFactory
    : IDbContextFactory<BlokeBotDbContext>,
        IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly DbContextOptions<BlokeBotDbContext> options;

    private SqliteBlokeBotDbFactory(SqliteConnection connection)
    {
        this.connection = connection;
        options = new DbContextOptionsBuilder<BlokeBotDbContext>().UseSqlite(connection).Options;
    }

    public static async Task<SqliteBlokeBotDbFactory> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new SqliteBlokeBotDbFactory(connection);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        return factory;
    }

    public BlokeBotDbContext CreateDbContext() => new(options);

    public ValueTask<BlokeBotDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(CreateDbContext());

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();
}
