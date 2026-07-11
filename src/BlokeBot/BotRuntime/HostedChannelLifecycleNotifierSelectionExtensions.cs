namespace BlokeBot.BotRuntime;

/// <summary>
/// Selects the hosted-channel lifecycle notifier owned by the BlokeBot feature.
/// </summary>
public static class HostedChannelLifecycleNotifierSelectionExtensions
{
    /// <summary>
    /// Uses persisted hosted-channel lifecycle tracking.
    /// </summary>
    public static TwitchBotChannelLifecycleNotifierSelection UseBlokeBotHostedChannelLifecycleNotifier(
        this TwitchBotChannelLifecycleNotifierSelection selection
    )
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.UseHostedNotifier<HostedChannelLifecycleNotifier>();
    }
}
