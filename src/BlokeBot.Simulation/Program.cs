using BlokeBot.Simulation;
using Serilog;

SimulationApplication.ConfigureBootstrapLogging();

try
{
    await using var simulation = await SimulationApplication.BuildAsync(
        args,
        CancellationToken.None
    );
    await simulation.App.InitializeSimulationAsync(CancellationToken.None);
    await simulation.App.StartAsync();
    await simulation
        .App.Services.GetRequiredService<SimulationStartupCoordinator>()
        .BootstrapAsync(simulation.App, CancellationToken.None);
    await simulation.App.WaitForShutdownAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
