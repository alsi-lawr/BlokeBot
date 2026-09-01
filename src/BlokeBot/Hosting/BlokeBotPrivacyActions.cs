using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Cli;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Privacy;
using Spectre.Console;

namespace BlokeBot.Hosting;

internal sealed record BlokeBotPrivacyOptions(
    string? Login,
    string? TwitchUserId,
    int? HostId,
    string? DataDirectory,
    string? ConfigurationPath
);

/// <summary>
/// Operator-side fulfilment for verified privacy requests received through the monitored privacy
/// contact. Runs directly against the deployment's database; there is deliberately no in-app
/// request form.
/// </summary>
internal static class BlokeBotPrivacyActions
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static async Task<int> ExportAsync(
        BlokeBotPrivacyOptions options,
        string? outputPath,
        IAnsiConsole console,
        CancellationToken ct
    ) =>
        await RunAsync(
            options,
            console,
            async (db, subject) =>
            {
                var export = await ViewerPrivacyService.ExportAsync(
                    db,
                    subject,
                    options.HostId,
                    ct
                );
                var json = JsonSerializer.Serialize(export.Sections, _json);
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    console.WriteLine(json);
                }
                else
                {
                    var fullPath = Path.GetFullPath(outputPath);
                    await File.WriteAllTextAsync(fullPath, json, ct);
                    console.WriteLine($"Wrote {export.Sections.Count} sections to {fullPath}.");
                }

                return 0;
            }
        );

    internal static async Task<int> EraseAsync(
        BlokeBotPrivacyOptions options,
        bool confirmed,
        IAnsiConsole console,
        CancellationToken ct
    )
    {
        if (!confirmed)
        {
            console.WriteLine(
                "blokebot: erasure permanently deletes and de-identifies this person's data. "
                    + "Re-run with --confirm to proceed."
            );
            return 1;
        }

        return await RunAsync(
            options,
            console,
            async (db, subject) =>
            {
                var report = await ViewerPrivacyService.EraseAsync(db, subject, options.HostId, ct);
                if (report.ChangedRows.Count == 0)
                {
                    console.WriteLine(
                        "No stored rows matched this identity. "
                            + "Erasure is idempotent; nothing remained to remove."
                    );
                    return 0;
                }

                foreach (var (section, rows) in report.ChangedRows.OrderBy(x => x.Key))
                {
                    console.WriteLine($"{section}: {rows}");
                }

                console.WriteLine(
                    $"Erased or de-identified {report.TotalChangedRows} rows. Existing backups "
                        + "age out on the configured snapshot schedule."
                );
                return 0;
            }
        );
    }

    private static async Task<int> RunAsync(
        BlokeBotPrivacyOptions options,
        IAnsiConsole console,
        Func<BlokeBotDbContext, PrivacySubject, Task<int>> action
    )
    {
        PrivacySubject subject;
        try
        {
            subject = PrivacySubject.Create(options.TwitchUserId, options.Login);
        }
        catch (ArgumentException)
        {
            console.WriteLine("blokebot: supply --login, --user-id, or both.");
            return 1;
        }

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(BlokeBotPrivacyActions).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
            }
        );
        BlokeBotHost.Configure(
            builder,
            new BlokeBotServeOptions(null, null, options.DataDirectory, options.ConfigurationPath)
        );
        BlokeBotMainDatabaseSettings databaseSettings;
        try
        {
            databaseSettings = BlokeBotMainDatabaseSettings.FromConfiguration(
                builder.Configuration
            );
        }
        catch (BlokeBotHostStartupException exception)
        {
            console.WriteLine(exception.Summary);
            return 1;
        }
        var statePaths = BlokeBotHost.ResolveStatePaths(
            builder.Configuration,
            options.DataDirectory,
            databaseSettings.Provider
        );
        if (
            databaseSettings.Provider == BlokeBotDatabaseProvider.Sqlite
            && !File.Exists(statePaths.DatabasePath)
        )
        {
            console.WriteLine($"blokebot: no database found at {statePaths.DatabasePath}.");
            return 1;
        }

        BlokeBotDatabaseConfiguration database;
        try
        {
            database = databaseSettings.CreateConfiguration(statePaths);
        }
        catch (BlokeBotHostStartupException exception)
        {
            console.WriteLine(exception.Summary);
            return 1;
        }
        await using var db = database.CreateDbContext();
        return await action(db, subject);
    }
}
