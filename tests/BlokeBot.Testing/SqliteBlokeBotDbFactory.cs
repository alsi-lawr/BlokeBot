using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Testing;

public sealed class SqliteBlokeBotDbFactory : IDbContextFactory<BlokeBotDbContext>, IAsyncDisposable
{
    private readonly SqliteConnection _keeperConnection;
    private readonly DbContextOptions<BlokeBotDbContext> _options;

    private SqliteBlokeBotDbFactory(SqliteConnection keeperConnection, string connectionString)
    {
        _keeperConnection = keeperConnection;
        _options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseSqlite(connectionString)
            .Options;
    }

    public static async Task<SqliteBlokeBotDbFactory> CreateAsync()
    {
        var factory = await CreateEmptyAsync();
        await using var db = factory.CreateDbContext();
        _ = await db.Database.EnsureCreatedAsync();
        return factory;
    }

    public static async Task<SqliteBlokeBotDbFactory> CreateEmptyAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"BlokeBotTests-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 0,
        }.ToString();
        var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        return new SqliteBlokeBotDbFactory(keeperConnection, connectionString);
    }

    public BlokeBotDbContext CreateDbContext() => new(_options);

    public ValueTask<BlokeBotDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(CreateDbContext());

    public async ValueTask DisposeAsync() => await _keeperConnection.DisposeAsync();
}
