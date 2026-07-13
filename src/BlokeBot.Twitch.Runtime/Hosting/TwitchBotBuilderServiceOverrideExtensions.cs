using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Declares feature-owned replacements for the Twitch runtime's default services.
/// </summary>
public static class TwitchBotBuilderServiceOverrideExtensions
{
    /// <summary>
    /// Replaces the default account provider with an already registered feature singleton.
    /// </summary>
    /// <typeparam name="TProvider">The feature-owned account provider.</typeparam>
    /// <param name="builder">The Twitch bot registration being configured.</param>
    /// <returns>The same builder for chained registration.</returns>
    public static ITwitchBotBuilder OverrideAccountProviderWith<TProvider>(
        this ITwitchBotBuilder builder
    )
        where TProvider : class, ITwitchBotAccountProvider
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<ITwitchBotAccountProvider>();
        builder.Services.AddSingleton<ITwitchBotAccountProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<TProvider>()
        );
        return builder;
    }

    /// <summary>
    /// Replaces the default command-response sender with an already registered feature singleton.
    /// </summary>
    /// <typeparam name="TSender">The feature-owned command-response sender.</typeparam>
    /// <param name="builder">The Twitch bot registration being configured.</param>
    /// <returns>The same builder for chained registration.</returns>
    public static ITwitchBotBuilder OverrideCommandResponseSenderWith<TSender>(
        this ITwitchBotBuilder builder
    )
        where TSender : class, ITwitchCommandResponseSender
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<ITwitchCommandResponseSender>();
        builder.Services.AddSingleton<ITwitchCommandResponseSender>(serviceProvider =>
            serviceProvider.GetRequiredService<TSender>()
        );
        return builder;
    }

    /// <summary>
    /// Replaces the no-op lifecycle notifier with an already registered feature singleton.
    /// </summary>
    /// <typeparam name="TNotifier">The feature-owned channel-lifecycle notifier.</typeparam>
    /// <param name="builder">The Twitch bot registration being configured.</param>
    /// <returns>The same builder for chained registration.</returns>
    public static ITwitchBotBuilder OverrideChannelLifecycleNotifierWith<TNotifier>(
        this ITwitchBotBuilder builder
    )
        where TNotifier : class, ITwitchBotChannelLifecycleNotifier
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<ITwitchBotChannelLifecycleNotifier>();
        builder.Services.AddSingleton<ITwitchBotChannelLifecycleNotifier>(serviceProvider =>
            serviceProvider.GetRequiredService<TNotifier>()
        );
        return builder;
    }
}
