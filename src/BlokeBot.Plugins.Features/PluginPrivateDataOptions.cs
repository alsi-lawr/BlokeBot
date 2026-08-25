using BlokeBot.Plugins.Contracts;
using Microsoft.Data.Sqlite;

namespace BlokeBot.Plugins.Features;

public sealed class PluginPrivateDataOptions
{
    public PluginPrivateDataOptions(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    internal string RootDirectory { get; }

    internal string DatabasePath(PluginId pluginId) =>
        Path.Combine(RootDirectory, pluginId.Value, "private.db");

    internal string ConnectionString(PluginId pluginId) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath(pluginId),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
}
