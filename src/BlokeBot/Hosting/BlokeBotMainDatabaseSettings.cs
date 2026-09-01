using BlokeBot.Persistence;

namespace BlokeBot.Hosting;

internal sealed record BlokeBotMainDatabaseSettings(
    BlokeBotDatabaseProvider Provider,
    string? PostgreSqlConnectionStringFile
)
{
    internal const string ProviderKey = "BlokeBot:DatabaseProvider";
    internal const string DatabasePathKey = "BlokeBot:DatabasePath";
    internal const string StateDirectoryKey = "BlokeBot:StateDirectory";
    internal const string PostgreSqlConnectionStringFileKey =
        "BlokeBot:PostgreSqlConnectionStringFile";

    internal static BlokeBotMainDatabaseSettings FromConfiguration(IConfiguration configuration)
    {
        var provider = configuration[ProviderKey] switch
        {
            null or "" => BlokeBotDatabaseProvider.Sqlite,
            "Sqlite" => BlokeBotDatabaseProvider.Sqlite,
            "PostgreSql" => BlokeBotDatabaseProvider.PostgreSql,
            _ => throw new BlokeBotHostStartupException(
                "blokebot: BlokeBot:DatabaseProvider must be Sqlite or PostgreSql."
            ),
        };
        var databasePath = Explicit(configuration[DatabasePathKey]);
        var connectionStringFile = Explicit(configuration[PostgreSqlConnectionStringFileKey]);
        return (provider, databasePath, connectionStringFile) switch
        {
            (BlokeBotDatabaseProvider.Sqlite, _, not null) =>
                throw new BlokeBotHostStartupException(
                    "blokebot: Sqlite cannot be combined with BlokeBot:PostgreSqlConnectionStringFile."
                ),
            (BlokeBotDatabaseProvider.PostgreSql, not null, _) =>
                throw new BlokeBotHostStartupException(
                    "blokebot: PostgreSql cannot be combined with BlokeBot:DatabasePath."
                ),
            (BlokeBotDatabaseProvider.PostgreSql, _, null) =>
                throw new BlokeBotHostStartupException(
                    "blokebot: PostgreSql requires BlokeBot:PostgreSqlConnectionStringFile."
                ),
            _ => new(provider, connectionStringFile),
        };
    }

    internal BlokeBotDatabaseConfiguration CreateConfiguration(BlokeBotStatePaths statePaths)
    {
        try
        {
            return Provider switch
            {
                BlokeBotDatabaseProvider.Sqlite => BlokeBotDatabaseConfiguration.Sqlite(
                    statePaths.DatabasePath
                ),
                BlokeBotDatabaseProvider.PostgreSql =>
                    BlokeBotDatabaseConfiguration.PostgreSqlFromFile(
                        PostgreSqlConnectionStringFile!
                    ),
            };
        }
        catch (BlokeBotDatabaseConfigurationException exception)
        {
            throw new BlokeBotHostStartupException($"blokebot: {exception.Message}");
        }
    }

    private static string? Explicit(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
