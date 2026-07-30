namespace BlokeBot.Core.Features.HostedChannels;

public interface INativeTwitchFeatureChangeObserver
{
    Task NativeTwitchFeatureChangedAsync(
        int hostId,
        NativeTwitchFeatureState state,
        CancellationToken cancellationToken
    );
}

public enum NativeTwitchFeatureState
{
    Disabled,
    Enabled,
}
