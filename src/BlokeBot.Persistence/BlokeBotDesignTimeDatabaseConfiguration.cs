namespace BlokeBot.Persistence;

public static class BlokeBotDesignTimeDatabaseConfiguration
{
    public const string PostgreSqlConnectionStringFileEnvironmentVariable =
        "BlokeBot__PostgreSqlConnectionStringFile";

    public static BlokeBotDatabaseConfiguration Parse(
        IReadOnlyList<string> args,
        BlokeBotDatabaseProvider expectedProvider
    )
    {
        var configuration =
            args.Count == 2 && string.Equals(args[0], "--provider", StringComparison.Ordinal)
                ? args[1] switch
                {
                    "Sqlite" => BlokeBotDatabaseConfiguration.Sqlite("blokebot.db"),
                    "PostgreSql" => PostgreSql(),
                    _ => throw ExplicitProviderRequired(),
                }
                : throw ExplicitProviderRequired();
        return configuration.Provider == expectedProvider
            ? configuration
            : throw new ArgumentException(
                $"This migration history requires --provider {expectedProvider}."
            );

        static ArgumentException ExplicitProviderRequired() =>
            new(
                "Design-time database operations require --provider Sqlite or --provider PostgreSql."
            );
    }

    private static BlokeBotDatabaseConfiguration PostgreSql()
    {
        var connectionStringFile = Environment.GetEnvironmentVariable(
            PostgreSqlConnectionStringFileEnvironmentVariable
        );
        return string.IsNullOrWhiteSpace(connectionStringFile)
            ? BlokeBotDatabaseConfiguration.PostgreSql(
                "Host=localhost;Database=blokebot;Username=blokebot"
            )
            : BlokeBotDatabaseConfiguration.PostgreSqlFromFile(connectionStringFile);
    }
}
