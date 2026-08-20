using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Simulation;

internal enum SimulationActivationMode
{
    Complete,
    Pending,
    Fail,
}

internal sealed class SimulationConfigurationActivationObserver : IConfigurationActivationObserver
{
    private SimulationActivationMode _mode;

    public void SetMode(SimulationActivationMode mode) => _mode = mode;

    public async ValueTask FeatureEnabledAsync(
        int hostId,
        HostFeatureFlags feature,
        CancellationToken cancellationToken
    )
    {
        if (_mode == SimulationActivationMode.Fail)
        {
            throw new InvalidOperationException("Planned Simulator activation failure.");
        }
        if (_mode == SimulationActivationMode.Pending)
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        }
    }
}
