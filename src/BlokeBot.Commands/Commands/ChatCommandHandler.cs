namespace BlokeBot.Commands;

/// <summary>
/// Handles a matched Twitch chat command.
/// </summary>
/// <param name="context">The chat command context.</param>
/// <param name="args">The command arguments after the command name.</param>
/// <param name="cancellationToken">A token that cancels command handling.</param>
/// <returns>A task that completes when command handling is finished.</returns>
public delegate ValueTask ChatCommandHandler(
    ChatCommandContext context,
    IReadOnlyList<string> args,
    CancellationToken cancellationToken
);

/// <summary>
/// Handles a command route that is resolved dynamically at dispatch time.
/// </summary>
/// <returns>The typed command handling outcome.</returns>
public delegate ValueTask<CommandHandlingOutcome> DynamicChatCommandHandler(
    ChatCommandContext context,
    IReadOnlyList<string> args,
    CancellationToken cancellationToken
);
