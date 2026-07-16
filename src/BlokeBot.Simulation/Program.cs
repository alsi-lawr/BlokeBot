using BlokeBot.Simulation;
using Serilog;

SimulationApplication.ConfigureBootstrapLogging();

try
{
    await using var app = SimulationApplication.Build(args);
    await app.InitializeSimulationAsync(CancellationToken.None);
    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
