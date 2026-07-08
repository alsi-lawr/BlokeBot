namespace BlokeBot.Commands;

/// <summary>
/// Handles a matched Twitch chat command.
/// </summary>
/// <param name="context">The chat command context.</param>
/// <param name="args">The command arguments after the command name.</param>
/// <param name="cancellationToken">A token that cancels command handling.</param>
/// <returns>A task that completes when command handling is finished.</returns>
public delegate ValueTask TwitchCommandHandler(
    TwitchCommandContext context,
    IReadOnlyList<string> args,
    CancellationToken cancellationToken
);

/// <summary>
/// Handles a command route that is resolved dynamically at dispatch time.
/// </summary>
/// <returns><see langword="true" /> when the dynamic route was handled.</returns>
public delegate ValueTask<bool> TwitchDynamicCommandHandler(
    TwitchCommandContext context,
    IReadOnlyList<string> args,
    CancellationToken cancellationToken
);
