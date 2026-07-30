namespace BlokeBot.Twitch.Runtime;

public interface INativeTwitchFeatureStateProvider
{
    ValueTask<bool> IsEnabledAsync(string channel, CancellationToken cancellationToken);
}
