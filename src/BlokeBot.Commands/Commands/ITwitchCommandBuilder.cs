namespace BlokeBot.Commands;

/// <summary>
/// Builds the command routes and filters used by a Twitch bot.
/// </summary>
public interface ITwitchCommandBuilder
{
    /// <summary>
    /// Registers a command route.
    /// </summary>
    /// <param name="route">The command name without the command prefix.</param>
    /// <param name="handler">The handler to invoke when the command is matched.</param>
    /// <returns>The same builder for chained registration.</returns>
    ITwitchCommandBuilder Map(string route, TwitchCommandHandler handler);

    /// <summary>
    /// Registers a command handler for unmatched command routes.
    /// </summary>
    /// <param name="handler">The handler to invoke when no mapped route is matched.</param>
    /// <returns>The same builder for chained registration.</returns>
    ITwitchCommandBuilder MapFallback(TwitchCommandHandler handler);

    /// <summary>
    /// Registers a command filter type.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <returns>The same builder for chained registration.</returns>
    ITwitchCommandBuilder UseFilter<TFilter>()
        where TFilter : class, ITwitchCommandFilter;
}
