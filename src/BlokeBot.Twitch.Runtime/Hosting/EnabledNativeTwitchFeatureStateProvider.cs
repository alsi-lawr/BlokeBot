namespace BlokeBot.Twitch.Runtime;

internal sealed class EnabledNativeTwitchFeatureStateProvider : INativeTwitchFeatureStateProvider
{
    public ValueTask<bool> IsEnabledAsync(
        string channel,
        NativeTwitchFeature feature,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(true);
}
