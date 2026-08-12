using BlokeBot.Cli;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Serilog;
using Spectre.Console;

namespace BlokeBot.Hosting;

internal static class BlokeBotHost
{
    internal static async Task<int> RunAsync(
        BlokeBotServeOptions options,
        IAnsiConsole console,
        CancellationToken cancellationToken
    )
    {
        BlokeBotHostLogging.ConfigureBootstrap();

        try
        {
            await using var composition = Create(options);
            await composition.App.InitializeBlokeBotPersistenceAsync(cancellationToken);
            await composition.App.StartAsync(cancellationToken);

            if (composition.Twitch.Mode == BlokeBotRuntimeMode.Offline)
            {
                console.WriteLine(composition.Twitch.OfflineGuidance());
            }

            var server = composition.App.Services.GetRequiredService<IServer>();
            var localUrl = BlokeBotServerUrlPolicy.LocalUrl(
                server.Features.Get<IServerAddressesFeature>()
            );
            console.WriteLine($"BlokeBot is available at {localUrl}");

            try
            {
                await composition.App.WaitForShutdownAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

            return 0;
        }
        catch (BlokeBotHostStartupException exception)
        {
            BlokeBotHostLogging.HostFailure(exception);
            console.WriteLine(exception.Summary);
            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            BlokeBotHostLogging.HostFailure(exception);
            console.WriteLine($"blokebot failed ({exception.GetType().Name}).");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    internal static BlokeBotHostComposition Create(BlokeBotServeOptions options)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(BlokeBotHost).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
            }
        );
        Configure(builder, options);

        var statePaths = ResolveStatePaths(builder.Configuration, options.DataDirectory);
        _ = builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["BlokeBot:DatabasePath"] = statePaths.DatabasePath,
                ["TwitchBot:Identity:TokenCachePath"] = statePaths.TokenCachePath,
            }
        );

        _ = builder.Host.UseSerilog(
            (context, services, logging) =>
                BlokeBotHostLogging.ConfigureProduction(
                    logging,
                    context.Configuration,
                    services,
                    statePaths.StateDirectory
                )
        );
        _ = builder.Services.AddBlokeBotPersistence(statePaths.DatabasePath);
        ConfigureDataProtection(builder.Services, statePaths);
        var twitch = BlokeBotTwitchModeSelection.FromConfiguration(builder.Configuration);
        _ = builder.AddBlokeBotCore(twitch.Mode);

        var app = builder.Build();
        _ = app.UseSerilogRequestLogging();
        _ = app.UseBlokeBotCore(twitch.Mode);
        return new BlokeBotHostComposition(app, twitch, statePaths);
    }

    internal static void Configure(WebApplicationBuilder builder, BlokeBotServeOptions options)
    {
        var environmentName = builder.Environment.EnvironmentName;
        builder.Configuration.Sources.Clear();
        _ = builder
            .Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["urls"] = BlokeBotServerUrlPolicy.DefaultUrl }
            )
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile(
                $"appsettings.{environmentName}.json",
                optional: true,
                reloadOnChange: false
            );

        if (!string.IsNullOrWhiteSpace(options.ConfigurationPath))
        {
            _ = builder.Configuration.AddJsonFile(
                ResolveConfigurationPath(options.ConfigurationPath, Environment.CurrentDirectory),
                optional: false,
                reloadOnChange: false
            );
        }

        _ = builder
            .Configuration.AddEnvironmentVariables("DOTNET_")
            .AddEnvironmentVariables("ASPNETCORE_")
            .AddEnvironmentVariables();

        if (options.Host is not null || options.Port is not null)
        {
            _ = builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["urls"] = BlokeBotServerUrlPolicy.ExplicitUrl(options.Host, options.Port),
                }
            );
        }
    }

    internal static string ResolveConfigurationPath(string path, string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        return Path.GetFullPath(
            Path.IsPathRooted(path) ? path : Path.Combine(currentDirectory, path)
        );
    }

    internal static BlokeBotStatePaths ResolveStatePaths(
        IConfiguration configuration,
        string? dataDirectory
    )
    {
        var (operatingSystem, platformEnvironment) = BlokeBotPlatformEnvironment.Current();
        var resolution = BlokeBotStatePathResolver.Resolve(
            new BlokeBotStatePathRequest(
                operatingSystem,
                platformEnvironment,
                dataDirectory,
                configuration["BlokeBot:DatabasePath"],
                configuration["TwitchBot:Identity:TokenCachePath"]
            )
        );
        if (resolution is BlokeBotStatePathResolution.Failed failure)
        {
            throw new BlokeBotHostStartupException($"blokebot: {failure.Message}");
        }

        var paths = ((BlokeBotStatePathResolution.Resolved)resolution).Paths;
        var preparation = BlokeBotStatePathPreparer.Prepare(paths);
        return preparation switch
        {
            BlokeBotStatePathPreparation.Prepared prepared => prepared.Paths,
            BlokeBotStatePathPreparation.Failed preparationFailure =>
                throw new BlokeBotHostStartupException(preparationFailure.Message),
            _ => throw new InvalidOperationException("Unknown state-path preparation result."),
        };
    }

    private static void ConfigureDataProtection(
        IServiceCollection services,
        BlokeBotStatePaths statePaths
    )
    {
        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName("BlokeBot")
            .PersistKeysToFileSystem(new DirectoryInfo(statePaths.DataProtectionKeysDirectory));
        if (OperatingSystem.IsWindows())
        {
            _ = dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }
    }
}

internal sealed record BlokeBotHostComposition(
    WebApplication App,
    BlokeBotTwitchModeSelection Twitch,
    BlokeBotStatePaths StatePaths
) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!App.Lifetime.ApplicationStopped.IsCancellationRequested)
            {
                await App.StopAsync();
            }
        }
        finally
        {
            await App.DisposeAsync();
        }
    }
}

internal sealed class BlokeBotHostStartupException(string summary)
    : Exception("BlokeBot host startup failed.")
{
    internal string Summary { get; } = summary;
}
