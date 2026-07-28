namespace BlokeBot.Twitch.Runtime;

internal sealed class EnabledNativeTwitchFeatureStateProvider : INativeTwitchFeatureStateProvider
{
    public ValueTask<bool> IsEnabledAsync(string channel, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(true);
    }
}
