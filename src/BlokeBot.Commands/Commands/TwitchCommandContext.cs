namespace BlokeBot.Commands;

/// <summary>
/// Provides command handlers with message, reply, and service access.
/// </summary>
public sealed record TwitchCommandContext
{
    private readonly Func<string, CancellationToken, ValueTask> reply;

    internal TwitchCommandContext(
        TwitchChatMessage message,
        string commandName,
        IServiceProvider services,
        Func<string, CancellationToken, ValueTask> reply
    )
    {
        Message = message;
        CommandName = commandName;
        Services = services;
        this.reply = reply;
    }

    /// <summary>
    /// Gets the received chat message.
    /// </summary>
    public TwitchChatMessage Message { get; }

    /// <summary>
    /// Gets the command name that matched the received chat message.
    /// </summary>
    public string CommandName { get; }

    /// <summary>
    /// Gets the service provider for command-specific service resolution.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Sends a chat reply to the configured channel.
    /// </summary>
    /// <param name="message">The reply text.</param>
    /// <param name="cancellationToken">A token that cancels the reply operation.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    public ValueTask ReplyAsync(string message, CancellationToken cancellationToken) =>
        reply(message, cancellationToken);
}
