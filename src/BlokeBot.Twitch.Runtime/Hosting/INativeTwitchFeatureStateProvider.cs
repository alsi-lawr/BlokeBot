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
    Polls,
    RewardsAndRedemptions,
    Predictions,
    RaidCollaboration,
}
