using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Commands;

/// <summary>
/// Builds Twitch bot service registrations.
/// </summary>
public interface IChatBotBuilder
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
    IChatBotBuilder AddCommands(Action<IChatCommandBuilder> configure);

    /// <summary>
    /// Registers commands from a module type.
    /// </summary>
    /// <typeparam name="TModule">The module type.</typeparam>
    /// <returns>The same builder for chained registration.</returns>
    IChatBotBuilder AddCommandModule<TModule>()
        where TModule : class, IChatCommandModule;

    /// <summary>
    /// Registers a command filter for explicit use by a command plan.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <returns>The same builder for chained registration.</returns>
    IChatBotBuilder AddCommandFilter<TFilter>()
        where TFilter : class, IChatCommandFilter;
}
