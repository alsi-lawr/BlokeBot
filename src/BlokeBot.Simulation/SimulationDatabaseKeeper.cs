using Microsoft.Data.Sqlite;

namespace BlokeBot.Simulation;

internal sealed class SimulationDatabaseKeeper : IDisposable
{
    private readonly SqliteConnection _connection;

    public SimulationDatabaseKeeper()
    {
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"blokebot-simulation-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();
        _connection = new SqliteConnection(ConnectionString);
        _connection.Open();
    }

    internal string ConnectionString { get; }

    internal bool IsOpen => _connection.State == System.Data.ConnectionState.Open;

    public void Dispose() => _connection.Dispose();
}
