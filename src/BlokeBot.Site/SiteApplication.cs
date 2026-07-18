using BlokeBot.Site.Components;
using Serilog;

namespace BlokeBot.Site;

internal static class SiteApplication
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

    public static WebApplication Build(
        string[] arguments,
        Action<LoggerConfiguration>? configureDefaults = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = arguments,
                ApplicationName = typeof(SiteApplication).Assembly.GetName().Name,
            }
        );
        builder.WebHost.UseStaticWebAssets();
        builder.Host.UseSerilog(
            (context, services, logging) =>
            {
                logging.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services);
                if (!context.Configuration.GetSection("Serilog:WriteTo").Exists())
                {
                    logging.Enrich.FromLogContext();
                    if (configureDefaults is null)
                    {
                        logging.WriteTo.Console(outputTemplate: ConsoleOutputTemplate);
                    }
                    else
                    {
                        configureDefaults(logging);
                    }
                }
            }
        );
        builder.Services.AddRazorComponents();

        var app = builder.Build();
        var pathBase = app.Configuration["BlokeBotSite:PathBase"];
        if (!string.IsNullOrWhiteSpace(pathBase))
        {
            app.UsePathBase(pathBase);
        }

        app.UseSerilogRequestLogging();
        app.MapMethods(
            "/favicon.ico",
            ["GET", "HEAD"],
            () => Results.Redirect("blokebot-mark.svg")
        );
        app.MapStaticAssets();
        app.MapRazorComponents<App>().DisableAntiforgery();
        return app;
    }
}
