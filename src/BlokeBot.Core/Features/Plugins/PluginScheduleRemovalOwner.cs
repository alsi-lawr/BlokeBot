using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

internal sealed class PluginScheduleRemovalOwner(IPluginScheduleStore schedules)
    : IPluginRemovalDataOwner
{
    public async ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
        PluginRemovalContext context,
        CancellationToken cancellationToken
    )
    {
        await schedules.RemovePluginAsync(context.PluginId, cancellationToken);
        return new PluginLifecycleOwnerOutcome.Succeeded();
    }
}
