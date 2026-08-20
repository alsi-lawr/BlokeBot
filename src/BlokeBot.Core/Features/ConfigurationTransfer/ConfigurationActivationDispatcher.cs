using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed class ConfigurationActivationDispatcher(
    IEnumerable<IHostFeatureChangeObserver> featureObservers,
    IEnumerable<IConfigurationActivationObserver> activationObservers,
    IEventSubChannelReconciliationTrigger? eventSub,
    HostedChannelChangeNotifier changes
)
{
    public async Task ActivateAsync(
        int hostId,
        HostFeatureFlags enabled,
        HostFeatureFlags disabled,
        CancellationToken cancellationToken
    )
    {
        foreach (var feature in HostFeatureCatalog.Features.Where(x => (enabled & x) == x))
        {
            foreach (var observer in activationObservers)
            {
                await observer.FeatureEnabledAsync(hostId, feature, cancellationToken);
            }
        }
        foreach (var feature in HostFeatureCatalog.Features.Where(x => (disabled & x) == x))
        {
            foreach (var observer in featureObservers)
            {
                await observer.FeatureChangedAsync(hostId, feature, false, cancellationToken);
            }
        }
        if ((enabled | disabled) != HostFeatureFlags.None && eventSub is not null)
        {
            await eventSub.ReconcileAsync(cancellationToken);
        }
        _ = await changes.NotifyChangedAsync(cancellationToken);
    }
}
