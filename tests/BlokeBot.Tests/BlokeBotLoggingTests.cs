using BlokeBot.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;

namespace BlokeBot.Tests;

[NotInParallel]
public sealed class BlokeBotLoggingTests
{
    [Test]
    public void DefaultProductionLogging_WritesCompactFileUnderDatabaseStateDirectory()
    {
        var stateDirectory = TemporaryDirectory();
        try
        {
            using var services = new ServiceCollection().BuildServiceProvider();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var loggerConfiguration = new LoggerConfiguration();
            BlokeBotHostLogging.ConfigureProduction(
                loggerConfiguration,
                configuration,
                services,
                stateDirectory
            );
            using var logger = loggerConfiguration.CreateLogger();

            BlokeBotHostLogging.HostFailure(
                logger,
                new InvalidOperationException("credential=do-not-log-this")
            );

            var logFile = Directory
                .EnumerateFiles(Path.Combine(stateDirectory, "logs"), "blokebot-*.json")
                .Single();
            var contents = File.ReadAllText(logFile);
            contents.ShouldContain("BlokeBot host terminated unexpectedly");
            contents.ShouldContain("InvalidOperationException");
            contents.ShouldNotContain("do-not-log-this");
            contents.ShouldNotContain("credential=");
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blokebot-log-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
