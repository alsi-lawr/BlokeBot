namespace BlokeBot.Twitch.Runtime;

public interface INativeTwitchFeatureStateProvider
{
    ValueTask<bool> IsEnabledAsync(
        string channel,
        NativeTwitchFeature feature,
        CancellationToken cancellationToken
    );
}

public enum NativeTwitchFeature
{
    Shoutouts,
    Polls,
    RewardsAndRedemptions,
    Predictions,
}
