namespace BlokeBot.Commands;

/// <summary>
/// Provides command handlers with message and typed response access.
/// </summary>
public sealed record ChatCommandContext
{
    /// <summary>
    /// Gets the received chat message.
    /// </summary>
    public required ChatMessage Message { get; init; }

    /// <summary>
    /// Gets the command name that matched the received chat message.
    /// </summary>
    public required string CommandName { get; init; }

    /// <summary>
    /// Sets the focused response collaborator for this dispatch.
    /// </summary>
    public required CommandResponder Responder { private get; init; }

    /// <summary>
    /// Sends a chat reply to the configured channel.
    /// </summary>
    /// <param name="message">The reply text.</param>
    /// <param name="cancellationToken">A token that cancels the reply operation.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    public ValueTask ReplyAsync(string message, CancellationToken cancellationToken)
    {
        return Responder(CommandResponse.Chat(message), cancellationToken);
    }

    public ValueTask RespondAsync(CommandResponse response, CancellationToken cancellationToken)
    {
        return Responder(response, cancellationToken);
    }
}
