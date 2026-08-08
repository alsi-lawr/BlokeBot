using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

/// <summary>
/// Reconciles EventSub subscriptions when the Automations switch changes so automation-only
/// subscriptions are created on enable and removed on disable.
/// </summary>
internal sealed class AutomationEventSubReconciliationObserver(
    IEventSubChannelReconciliationTrigger? eventSub = null
) : IHostFeatureChangeObserver
{
    public async ValueTask FeatureChangedAsync(
        int hostId,
        HostFeatureFlags feature,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        if (feature == HostFeatureFlags.Automations && eventSub is not null)
        {
            await eventSub.ReconcileAsync(cancellationToken);
        }
    }
}
