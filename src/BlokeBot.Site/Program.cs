using BlokeBot.Site;
using Serilog;

SiteApplication.ConfigureBootstrapLogging();

try
{
    await using var app = SiteApplication.Build(args);
    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
