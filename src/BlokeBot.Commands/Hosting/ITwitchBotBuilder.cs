using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Commands;

/// <summary>
/// Builds Twitch bot service registrations.
/// </summary>
public interface ITwitchBotBuilder
{
    /// <summary>
    /// Gets the service collection being configured.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers commands with a fluent callback.
    /// </summary>
    /// <param name="configure">The command registration callback.</param>
    /// <returns>The same builder for chained registration.</returns>
    ITwitchBotBuilder AddCommands(Action<ITwitchCommandBuilder> configure);

    /// <summary>
    /// Registers commands from a module type.
    /// </summary>
    /// <typeparam name="TModule">The module type.</typeparam>
    /// <returns>The same builder for chained registration.</returns>
    ITwitchBotBuilder AddCommandModule<TModule>()
        where TModule : class, ITwitchCommandModule;

    /// <summary>
    /// Registers a command filter for explicit use by a command plan.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <returns>The same builder for chained registration.</returns>
    ITwitchBotBuilder AddCommandFilter<TFilter>()
        where TFilter : class, ITwitchCommandFilter;
}
