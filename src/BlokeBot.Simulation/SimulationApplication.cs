using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using BlokeBot.Simulation.FakeTwitch;
using BlokeBot.Twitch;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace BlokeBot.Simulation;

internal static class SimulationApplication
{
    internal const string ConsoleOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}";

    internal static void ConfigureBootstrapLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: ConsoleOutputTemplate)
            .CreateBootstrapLogger();
    }

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
            builder.Configuration.AddInMemoryCollection(
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
            builder.WebHost.UseStaticWebAssets();
            builder.Host.UseSerilog(
                (context, services, logging) =>
                {
                    logging
                        .ReadFrom.Configuration(context.Configuration)
                        .ReadFrom.Services(services);
                    if (!context.Configuration.GetSection("Serilog:WriteTo").Exists())
                    {
                        logging
                            .Enrich.FromLogContext()
                            .WriteTo.Console(outputTemplate: ConsoleOutputTemplate);
                    }
                }
            );

            builder.Services.AddSingleton<SimulationDatabaseKeeper>();
            builder.Services.AddBlokeBotPersistence(services =>
                services.GetRequiredService<SimulationDatabaseKeeper>().ConnectionString
            );
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddFakeTwitch(fakeTwitch.Authority);
            builder.AddBlokeBotCore(BlokeBotRuntimeMode.Online);
            builder.Services.AddBlokeBotSimulation();

            var app = builder.Build();
            app.UseSerilogRequestLogging();
            app.UseBlokeBotCore(BlokeBotRuntimeMode.Online);
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
                        argument == "--urls" && index + 1 < arguments.Length ? arguments[index + 1]
                        : argument.StartsWith("--urls=", StringComparison.Ordinal)
                            ? argument["--urls=".Length..]
                        : null
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
        await app
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
        await App.DisposeAsync();
        await FakeTwitch.DisposeAsync();
    }
}
