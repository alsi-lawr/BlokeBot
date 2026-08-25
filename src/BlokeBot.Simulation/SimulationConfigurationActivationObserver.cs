using BlokeBot.Core.Features.HostedChannels;

namespace BlokeBot.Simulation;

internal enum SimulationActivationMode
{
    Complete,
    Pending,
    Fail,
}

internal sealed class SimulationConfigurationActivationObserver : IHostFeatureActivationObserver
{
    private SimulationActivationMode _mode;

    public void SetMode(SimulationActivationMode mode) => _mode = mode;

    public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    )
    {
        if (_mode == SimulationActivationMode.Fail)
        {
            return new HostFeatureAutomaticWorkResult.Failed(
                new(
                    "simulation-activation-failed",
                    "The Simulator kept the imported configuration but could not activate its selected features."
                )
            );
        }
        if (_mode == SimulationActivationMode.Pending)
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        }

        return new HostFeatureAutomaticWorkResult.Complete();
    }
}
