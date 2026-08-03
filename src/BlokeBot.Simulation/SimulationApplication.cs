using System.Globalization;
using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using BlokeBot.Simulation.FakeTwitch;
using BlokeBot.Twitch;
using Microsoft.AspNetCore.DataProtection;
using Serilog;

namespace BlokeBot.Simulation;

internal static class SimulationApplication
{
    internal const string ConsoleOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}";

    internal static void ConfigureBootstrapLogging() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: ConsoleOutputTemplate
            )
            .CreateBootstrapLogger();

    internal static async Task<SimulationApplicationHost> BuildAsync(
        string[] arguments,
        CancellationToken cancellationToken
    )
    {
        var scenario = SimulationMode.SelectScenario(arguments);
        var dashboardOrigin = DashboardOrigin(arguments);
        var fakeTwitch = await FakeTwitchHost.StartAsync(scenario, cancellationToken);
        try
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    Args = arguments,
                    ApplicationName = typeof(SimulationApplication).Assembly.GetName().Name,
                    ContentRootPath = AppContext.BaseDirectory,
                    EnvironmentName = SimulationMode.EnvironmentName,
                }
            );
            _ = builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{TwitchEndpointPolicy.ConfigurationSectionName}:OAuthOrigin"] = new Uri(
                        fakeTwitch.Origin,
                        "oauth2/"
                    ).AbsoluteUri,
                    [$"{TwitchEndpointPolicy.ConfigurationSectionName}:HelixOrigin"] = new Uri(
                        fakeTwitch.Origin,
                        "helix/"
                    ).AbsoluteUri,
                    [$"{TwitchEndpointPolicy.ConfigurationSectionName}:EventSubWebSocketUri"] =
                        new UriBuilder(fakeTwitch.Origin) { Scheme = "ws", Path = "ws" }
                            .Uri
                            .AbsoluteUri,
                    ["TwitchBot:Identity:BotUsername"] = scenario.BotUser.Login,
                    ["TwitchBot:Identity:ClientId"] = scenario.ClientId,
                    ["TwitchBot:Identity:ClientSecret"] = "fake-twitch-secret",
                    ["TwitchBot:Identity:RedirectUri"] = new Uri(
                        dashboardOrigin,
                        "oauth/callback"
                    ).AbsoluteUri,
                }
            );
            _ = builder.WebHost.UseStaticWebAssets();
            _ = builder.Host.UseSerilog(
                (context, services, logging) =>
                {
                    _ = logging
                        .ReadFrom.Configuration(context.Configuration)
                        .ReadFrom.Services(services);
                    if (!context.Configuration.GetSection("Serilog:WriteTo").Exists())
                    {
                        _ = logging
                            .Enrich.FromLogContext()
                            .WriteTo.Console(
                                formatProvider: CultureInfo.InvariantCulture,
                                outputTemplate: ConsoleOutputTemplate
                            );
                    }
                }
            );

            _ = builder.Services.AddSingleton<SimulationDatabaseKeeper>();
            _ = builder.Services.AddBlokeBotPersistence(services =>
                services.GetRequiredService<SimulationDatabaseKeeper>().ConnectionString
            );
            _ = builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            _ = builder.Services.AddFakeTwitch(fakeTwitch.Authority);
            _ = builder.AddBlokeBotCore(BlokeBotRuntimeMode.Online);
            _ = builder.Services.AddBlokeBotSimulation();

            var app = builder.Build();
            _ = app.UseSerilogRequestLogging();
            _ = app.UseBlokeBotCore(BlokeBotRuntimeMode.Online);
            app.MapSimulationEndpoints();
            return new SimulationApplicationHost(app, fakeTwitch, scenario);
        }
        catch
        {
            await fakeTwitch.DisposeAsync();
            throw;
        }
    }

    private static Uri DashboardOrigin(string[] arguments)
    {
        var configured =
            arguments
                .Select(
                    (argument, index) =>
                        argument switch
                        {
                            "--urls" when index + 1 < arguments.Length => arguments[index + 1],
                            _ when argument.StartsWith("--urls=", StringComparison.Ordinal) =>
                                argument["--urls=".Length..],
                            _ => null,
                        }
                )
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? "http://127.0.0.1:5080";
        var origin = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .FirstOrDefault(uri => uri?.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
        return origin is null
            ? throw new InvalidOperationException(
                "Simulation requires a loopback HTTP dashboard listener."
            )
            : new Uri(origin.GetLeftPart(UriPartial.Authority) + "/");
    }

    internal static async Task InitializeSimulationAsync(
        this WebApplication app,
        CancellationToken cancellationToken
    )
    {
        await app.InitializeBlokeBotPersistenceAsync(cancellationToken);
        _ = await app
            .Services.GetRequiredService<SimulationFixtureSeeder>()
            .SeedAsync(cancellationToken);
        app.Services.GetRequiredService<SimulationReadiness>().MarkPersistenceReady();
    }
}

internal sealed class SimulationApplicationHost(
    WebApplication app,
    FakeTwitchHost fakeTwitch,
    FakeTwitchScenarioDefinition scenario
) : IAsyncDisposable
{
    public WebApplication App { get; } = app;

    public FakeTwitchHost FakeTwitch { get; } = fakeTwitch;

    public FakeTwitchScenarioDefinition Scenario { get; } = scenario;

    public async ValueTask DisposeAsync()
    {
        try
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
        finally
        {
            await FakeTwitch.DisposeAsync();
        }
    }
}
