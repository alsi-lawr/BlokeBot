using BlokeBot.Cli;
using BlokeBot.DatabaseCutover;
using BlokeBot.Persistence;
using Spectre.Console;

namespace BlokeBot.Hosting;

internal static class BlokeBotDatabaseCutoverActions
{
    internal static async Task<int> RunAsync(
        BlokeBotDatabaseCutoverSettings settings,
        IAnsiConsole console,
        CancellationToken cancellationToken
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(BlokeBotDatabaseCutoverActions).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
            }
        );
        BlokeBotHost.Configure(
            builder,
            new BlokeBotServeOptions(null, null, settings.DataDirectory, settings.ConfigurationPath)
        );

        BlokeBotMainDatabaseSettings databaseSettings;
        BlokeBotStatePaths statePaths;
        try
        {
            databaseSettings = BlokeBotMainDatabaseSettings.FromConfiguration(
                builder.Configuration
            );
            if (databaseSettings.Provider != BlokeBotDatabaseProvider.Sqlite)
            {
                console.WriteLine(
                    "blokebot: active configuration must remain on SQLite during cutover."
                );
                return 1;
            }

            statePaths = BlokeBotHost.ResolveStatePaths(
                builder.Configuration,
                settings.DataDirectory,
                BlokeBotDatabaseProvider.Sqlite,
                prepareDatabaseFile: false
            );
        }
        catch (BlokeBotHostStartupException exception)
        {
            console.WriteLine(exception.Summary);
            return 1;
        }

        if (!File.Exists(statePaths.DatabasePath))
        {
            console.WriteLine($"blokebot: no SQLite database found at {statePaths.DatabasePath}.");
            return 1;
        }

        var result = await new DatabaseCutoverRunner().RunAsync(
            new DatabaseCutoverOptions(
                statePaths.StateDirectory,
                statePaths.DatabasePath,
                settings.PostgreSqlConnectionStringFile,
                settings.OperationId,
                settings.BatchSize
            ),
            cancellationToken
        );
        return result.Match(
            succeeded =>
            {
                console.WriteLine(
                    succeeded.AlreadyComplete
                        ? $"Cutover {succeeded.OperationId} is already verified and complete."
                        : $"Cutover {succeeded.OperationId} copied and verified the PostgreSql target."
                );
                console.WriteLine($"Receipt: {succeeded.ReceiptPath}");
                console.WriteLine(
                    "SQLite is still active. Change DatabaseProvider and restart BlokeBot explicitly to use PostgreSql."
                );
                return 0;
            },
            rejected =>
            {
                console.WriteLine($"blokebot: {rejected.Message}");
                return 1;
            },
            failed =>
            {
                console.WriteLine($"blokebot: {failed.Message}");
                return 1;
            }
        );
    }
}
