namespace BlokeBot.Features.HostedChannels.Authorization;

/// <summary>
/// Selects the hosted-channel account provider owned by the BlokeBot feature.
/// </summary>
public static class HostedChannelAccountProviderSelectionExtensions
{
    /// <summary>
    /// Uses per-channel hosted account authorization for Twitch bot operations.
    /// </summary>
    public static TwitchBotAccountProviderSelection UseBlokeBotHostedChannelProvider(
        this TwitchBotAccountProviderSelection selection
    )
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.UseHostedChannelProvider<HostBotAccountAuthorizationService>();
    }
}
