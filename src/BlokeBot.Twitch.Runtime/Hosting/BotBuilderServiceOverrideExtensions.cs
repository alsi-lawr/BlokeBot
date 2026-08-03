using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Declares feature-owned replacements for the Twitch runtime's default services.
/// </summary>
public static class BotBuilderServiceOverrideExtensions
{
    /// <summary>
    /// Replaces the default account provider with an already registered feature singleton.
    /// </summary>
    /// <typeparam name="TProvider">The feature-owned account provider.</typeparam>
    /// <param name="builder">The Twitch bot registration being configured.</param>
    /// <returns>The same builder for chained registration.</returns>
    public static IChatBotBuilder OverrideAccountProviderWith<TProvider>(
        this IChatBotBuilder builder
    )
        where TProvider : class, IBotAccountProvider
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.Services.RemoveAll<IBotAccountProvider>();
        _ = builder.Services.AddSingleton<IBotAccountProvider>(static serviceProvider =>
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
    public static IChatBotBuilder OverrideCommandResponseSenderWith<TSender>(
        this IChatBotBuilder builder
    )
        where TSender : class, ICommandResponseSender
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.Services.RemoveAll<ICommandResponseSender>();
        _ = builder.Services.AddSingleton<ICommandResponseSender>(static serviceProvider =>
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
    public static IChatBotBuilder OverrideChannelLifecycleNotifierWith<TNotifier>(
        this IChatBotBuilder builder
    )
        where TNotifier : class, IBotChannelLifecycleNotifier
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.Services.RemoveAll<IBotChannelLifecycleNotifier>();
        _ = builder.Services.AddSingleton<IBotChannelLifecycleNotifier>(static serviceProvider =>
            serviceProvider.GetRequiredService<TNotifier>()
        );
        return builder;
    }
}
