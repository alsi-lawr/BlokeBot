namespace BlokeBot.Core.Hosting;

internal static class BlokeBotLogging
{
    internal static void Configure(ILoggingBuilder logging) =>
        logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
}
