namespace BlokeBot.Commands;

/// <summary>
/// Builds the command routes and filters used by a Twitch bot.
/// </summary>
public interface IChatCommandBuilder
{
    /// <summary>
    /// Registers a command route.
    /// </summary>
    /// <param name="route">The command name without the command prefix.</param>
    /// <param name="handler">The handler to invoke when the command is matched.</param>
    /// <returns>The same builder for chained registration.</returns>
    IChatCommandBuilder Map(string route, ChatCommandHandler handler);

    /// <summary>
    /// Registers a dynamic command handler for routes resolved at dispatch time.
    /// </summary>
    /// <param name="handler">The handler to invoke for unmatched static routes.</param>
    /// <returns>The same builder for chained registration.</returns>
    IChatCommandBuilder MapDynamic(DynamicChatCommandHandler handler);

    /// <summary>
    /// Registers a command handler for unmatched command routes.
    /// </summary>
    /// <param name="handler">The handler to invoke when no mapped route is matched.</param>
    /// <returns>The same builder for chained registration.</returns>
    IChatCommandBuilder MapFallback(ChatCommandHandler handler);

    /// <summary>
    /// Registers a command filter type.
    /// </summary>
    /// <typeparam name="TFilter">The filter type.</typeparam>
    /// <returns>The same builder for chained registration.</returns>
    IChatCommandBuilder UseFilter<TFilter>()
        where TFilter : class, IChatCommandFilter;
}
