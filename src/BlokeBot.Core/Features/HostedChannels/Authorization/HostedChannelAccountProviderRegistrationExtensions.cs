namespace BlokeBot.Core.Features.HostedChannels.Authorization;

/// <summary>
/// Configures the hosted-channel account provider owned by the BlokeBot feature.
/// </summary>
public static class HostedChannelAccountProviderRegistrationExtensions
{
    /// <summary>
    /// Uses per-channel hosted account authorization for Twitch bot operations.
    /// </summary>
    public static IChatBotBuilder UseBlokeBotHostedChannelProvider(this IChatBotBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.OverrideAccountProviderWith<HostBotAccountAuthorizationService>();
    }
}
