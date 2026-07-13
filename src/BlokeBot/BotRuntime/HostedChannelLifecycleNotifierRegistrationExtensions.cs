namespace BlokeBot.BotRuntime;

/// <summary>
/// Configures the hosted-channel lifecycle notifier owned by the BlokeBot feature.
/// </summary>
public static class HostedChannelLifecycleNotifierRegistrationExtensions
{
    /// <summary>
    /// Uses persisted hosted-channel lifecycle tracking.
    /// </summary>
    public static IChatBotBuilder UseBlokeBotHostedChannelLifecycleNotifier(
        this IChatBotBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.OverrideChannelLifecycleNotifierWith<HostedChannelLifecycleNotifier>();
    }
}
