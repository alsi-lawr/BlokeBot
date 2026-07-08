namespace BlokeBot.Commands;

/// <summary>
/// Decides whether a chat command is allowed to continue.
/// </summary>
public interface ITwitchCommandFilter
{
    /// <summary>
    /// Evaluates a command context.
    /// </summary>
    /// <param name="context">The command context.</param>
    /// <param name="cancellationToken">A token that cancels filter evaluation.</param>
    /// <returns><see langword="true" /> when command handling may continue.</returns>
    ValueTask<bool> AllowAsync(TwitchCommandContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);
}
