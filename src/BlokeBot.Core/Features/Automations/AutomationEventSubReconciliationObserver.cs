using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationEventSubReconciliationObserver(
    IEventSubChannelReconciliationTrigger? eventSub = null
) : IHostFeatureActivationObserver
{
    public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    )
    {
        if (change.Feature == HostFeatureFlags.Automations && eventSub is not null)
        {
            await eventSub.ReconcileAsync(cancellationToken);
        }

        return new HostFeatureAutomaticWorkResult.Complete();
    }
}
