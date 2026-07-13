namespace BlokeBot.Commands;

/// <summary>
/// Provides command handlers with message and typed response access.
/// </summary>
public sealed record TwitchCommandContext
{
    /// <summary>
    /// Gets the received chat message.
    /// </summary>
    public required TwitchChatMessage Message { get; init; }

    /// <summary>
    /// Gets the command name that matched the received chat message.
    /// </summary>
    public required string CommandName { get; init; }

    /// <summary>
    /// Sets the focused response collaborator for this dispatch.
    /// </summary>
    public required TwitchCommandResponder Responder { private get; init; }

    /// <summary>
    /// Sends a chat reply to the configured channel.
    /// </summary>
    /// <param name="message">The reply text.</param>
    /// <param name="cancellationToken">A token that cancels the reply operation.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    public ValueTask ReplyAsync(string message, CancellationToken cancellationToken)
    {
        return Responder(TwitchCommandResponse.Chat(message), cancellationToken);
    }

    public ValueTask RespondAsync(
        TwitchCommandResponse response,
        CancellationToken cancellationToken
    )
    {
        return Responder(response, cancellationToken);
    }
}
