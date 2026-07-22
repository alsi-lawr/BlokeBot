namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Provides the startup chat message effective for a Twitch channel.
/// </summary>
public interface IStartupChatMessageProvider
{
    /// <summary>
    /// Loads the setting effective when a fresh channel session is established.
    /// </summary>
    ValueTask<StartupChatMessage> GetAsync(string channel, CancellationToken cancellationToken);
}

/// <summary>
/// Describes whether a fresh channel session should send a startup chat message.
/// </summary>
public abstract record StartupChatMessage
{
    private StartupChatMessage() { }

    /// <summary>
    /// Startup chat delivery is disabled.
    /// </summary>
    public sealed record Disabled : StartupChatMessage;

    /// <summary>
    /// Startup chat delivery is enabled with the supplied normalized text.
    /// </summary>
    public sealed record Enabled(string Text) : StartupChatMessage;
}

internal sealed class ConfiguredStartupChatMessageProvider(BotSettings settings)
    : IStartupChatMessageProvider
{
    public ValueTask<StartupChatMessage> GetAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        return ValueTask.FromResult<StartupChatMessage>(
            string.IsNullOrWhiteSpace(settings.StartupMessage)
                ? new StartupChatMessage.Disabled()
                : new StartupChatMessage.Enabled(settings.StartupMessage.Trim())
        );
    }
}
