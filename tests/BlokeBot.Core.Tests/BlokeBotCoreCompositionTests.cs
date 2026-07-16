using BlokeBot.Core.BotStatus;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using BlokeBot.Twitch.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class BlokeBotCoreCompositionTests
{
    [Test]
    public async Task OfflineMode_ComposesOfflineRuntimeBehavior()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var app = Build(BlokeBotRuntimeMode.Offline, databasePath, []);

            app.Services.GetRequiredService<IBotRuntimeStatusAccessor>()
                .ShouldBeOfType<OfflineBotStatusAccessor>();
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Test]
    public async Task OnlineMode_ComposesTwitchRuntimeBehavior()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using var app = Build(
                BlokeBotRuntimeMode.Online,
                databasePath,
                new Dictionary<string, string?>
                {
                    ["TwitchBot:Identity:BotUsername"] = "configured-bot",
                    ["TwitchBot:Identity:ClientId"] = "configured-client",
                    ["TwitchBot:Identity:ClientSecret"] = "configured-secret",
                    ["TwitchBot:Identity:RedirectUri"] = "http://127.0.0.1/oauth/callback",
                }
            );

            app.Services.GetRequiredService<IBotRuntimeStatusAccessor>()
                .ShouldBeOfType<BotRuntimeStatusStore>();
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static WebApplication Build(
        BlokeBotRuntimeMode mode,
        string databasePath,
        IEnumerable<KeyValuePair<string, string?>> configuration
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" }
        );
        builder
            .Configuration.AddJsonFile(
                Path.Combine(RepositoryRoot(), "src", "BlokeBot", "appsettings.json"),
                optional: false,
                reloadOnChange: false
            )
            .AddInMemoryCollection(configuration);
        builder.Services.AddBlokeBotPersistence(databasePath);
        builder.AddBlokeBotCore(mode);
        return builder.Build();
    }

    private static string TemporaryDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"blokebot-core-tests-{Guid.NewGuid():N}.db");
    }

    private static void DeleteDatabase(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            File.Delete(databasePath + suffix);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlokeBot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
