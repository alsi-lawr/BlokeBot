using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Commands;

/// <summary>
/// Provides command handlers with message, reply, and service access.
/// </summary>
public sealed record TwitchCommandContext
{
    private readonly bool resolveReplyTarget;
    private readonly Func<TwitchCommandResponse, CancellationToken, ValueTask> respond;

    internal TwitchCommandContext(
        TwitchChatMessage message,
        string commandName,
        IServiceProvider services,
        Func<string, CancellationToken, ValueTask> reply
    )
        : this(
            message,
            commandName,
            services,
            (response, cancellationToken) => reply(response.Message, cancellationToken),
            false
        ) { }

    internal TwitchCommandContext(
        TwitchChatMessage message,
        string commandName,
        IServiceProvider services,
        Func<TwitchCommandResponse, CancellationToken, ValueTask> respond,
        bool resolveReplyTarget
    )
    {
        Message = message;
        CommandName = commandName;
        Services = services;
        this.respond = respond;
        this.resolveReplyTarget = resolveReplyTarget;
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
    public async ValueTask ReplyAsync(string message, CancellationToken cancellationToken)
    {
        var target = TwitchCommandResponseTarget.Chat;
        if (
            resolveReplyTarget
            && Services.GetService<ITwitchCommandResponseTargetResolver>() is { } resolver
        )
        {
            target = await resolver.ResolveAsync(this, cancellationToken);
        }

        await respond(new TwitchCommandResponse(target, message), cancellationToken);
    }

    public ValueTask RespondAsync(
        TwitchCommandResponse response,
        CancellationToken cancellationToken
    ) => respond(response, cancellationToken);
}
