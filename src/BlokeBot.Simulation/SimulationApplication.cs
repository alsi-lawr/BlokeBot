using BlokeBot.Core.Hosting;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.DataProtection;
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

    internal static WebApplication Build(string[] arguments)
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
        builder.WebHost.UseStaticWebAssets();
        builder.Host.UseSerilog(
            (context, services, logging) =>
            {
                logging.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services);
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
        var runtime = BlokeBotRuntimeMode.Offline;
        builder.AddBlokeBotCore(runtime);
        builder.Services.AddBlokeBotSimulation();

        var app = builder.Build();
        app.UseSerilogRequestLogging();
        app.UseBlokeBotCore(runtime);
        app.MapSimulationEndpoints();
        return app;
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
    }
}
