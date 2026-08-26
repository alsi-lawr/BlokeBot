using BlokeBot.Core.Features.Automations;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginFeatureWorkCoordinator(
    PluginDispatchWorkRegistry work,
    IPluginScheduleStore schedules,
    PluginAutomationRunCoordinator? automations = null
) : IPluginFeatureWorkCoordinator
{
    public async ValueTask CancelAndDrainAsync(
        PluginFeatureState state,
        CancellationToken cancellationToken
    )
    {
        await work.CancelAndDrainAsync(state, cancellationToken);
        if (automations is not null)
        {
            await automations.CancelAsync(state, cancellationToken);
        }
        await schedules.RemoveFeatureAsync(
            state.Key,
            new(state.Fence, state.Generation),
            cancellationToken
        );
    }

    public async ValueTask CancelAndDrainPluginAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        await work.CancelAndDrainPluginAsync(pluginId, cancellationToken);
        if (automations is not null)
        {
            await automations.CancelPluginAsync(pluginId, cancellationToken);
        }
        await schedules.RemovePluginAsync(pluginId, cancellationToken);
    }

    public void Resume(PluginFeatureState state) => work.Resume(state);
}
