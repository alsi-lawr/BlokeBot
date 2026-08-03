using System.Globalization;
using Serilog;
using Serilog.Formatting.Compact;

namespace BlokeBot.Hosting;

internal static class BlokeBotHostLogging
{
    internal const long FileSizeLimitBytes = 25 * 1024 * 1024;
    internal const int RetainedFileCountLimit = 14;
    internal const string ConsoleOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}";

    internal static string DefaultLogPath(string stateDirectory) =>
        Path.Combine(stateDirectory, "logs", "blokebot-.json");

    internal static void ConfigureBootstrap() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: ConsoleOutputTemplate
            )
            .CreateBootstrapLogger();

    internal static void ConfigureProduction(
        LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        IServiceProvider services,
        string stateDirectory
    )
    {
        _ = loggerConfiguration.ReadFrom.Configuration(configuration).ReadFrom.Services(services);

        if (configuration.GetSection("Serilog:WriteTo").Exists())
        {
            return;
        }

        _ = loggerConfiguration
            .Enrich.FromLogContext()
            .WriteTo.Console(
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: ConsoleOutputTemplate
            )
            .WriteTo.File(
                new CompactJsonFormatter(),
                DefaultLogPath(stateDirectory),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCountLimit,
                shared: false
            );
    }

    internal static void HostFailure(Exception exception) => HostFailure(Log.Logger, exception);

    internal static void HostFailure(Serilog.ILogger logger, Exception exception) =>
        logger
            .ForContext("ErrorType", exception.GetType().FullName)
            .Error("BlokeBot host terminated unexpectedly");
}
