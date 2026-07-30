using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.HostedChannels;

public interface INativeTwitchFeatureChangeObserver
{
    Task NativeTwitchFeatureChangedAsync(
        int hostId,
        HostFeatureFlags feature,
        NativeTwitchFeatureState state,
        CancellationToken cancellationToken
    );
}

public enum NativeTwitchFeatureState
{
    Disabled,
    Enabled,
}
