using System.Globalization;
using BlokeBot.Site.Components;
using Serilog;

namespace BlokeBot.Site;

internal static class SiteApplication
{
    internal const string ConsoleOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}";

    internal static void ConfigureBootstrapLogging() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                outputTemplate: ConsoleOutputTemplate,
                formatProvider: CultureInfo.InvariantCulture
            )
            .CreateBootstrapLogger();

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
        _ = builder.WebHost.UseStaticWebAssets();
        _ = builder.Host.UseSerilog(
            (context, services, logging) =>
            {
                _ = logging
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services);
                if (!context.Configuration.GetSection("Serilog:WriteTo").Exists())
                {
                    _ = logging.Enrich.FromLogContext();
                    if (configureDefaults is null)
                    {
                        _ = logging.WriteTo.Console(
                            outputTemplate: ConsoleOutputTemplate,
                            formatProvider: CultureInfo.InvariantCulture
                        );
                    }
                    else
                    {
                        configureDefaults(logging);
                    }
                }
            }
        );
        var options = builder
            .Services.AddOptions<BlokeBotSiteOptions>()
            .BindConfiguration("BlokeBotSite")
            .Validate(
                BlokeBotSiteOptionsValidation.HasValidLiveAppUrl,
                BlokeBotSiteOptionsValidation.LiveAppUrlFailure
            )
            .ValidateOnStart();
        if (!builder.Environment.IsDevelopment())
        {
            _ = options.Validate(
                BlokeBotSiteOptionsValidation.HasCompletePrivacyConfiguration,
                BlokeBotSiteOptionsValidation.PrivacyConfigurationFailure
            );
        }
        _ = builder.Services.AddSingleton(SiteProductVersion.Current);
        _ = builder.Services.AddRazorComponents();

        var app = builder.Build();
        var pathBase = app.Configuration["BlokeBotSite:PathBase"];
        if (!string.IsNullOrWhiteSpace(pathBase))
        {
            _ = app.UsePathBase(pathBase);
        }

        _ = app.UseSerilogRequestLogging();
        _ = app.MapMethods(
            "/favicon.ico",
            ["GET", "HEAD"],
            () => Results.Redirect("blokebot-mark.svg")
        );
        _ = app.MapStaticAssets();
        _ = app.MapRazorComponents<App>().DisableAntiforgery();
        return app;
    }
}
