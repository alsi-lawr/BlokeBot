namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Immutable IRC connection settings consumed by the runtime.
/// </summary>
public sealed record TwitchBotConnectionSettings
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public required bool UseTls { get; init; }
}

/// <summary>
/// Immutable public chat queue alert settings consumed by the runtime.
/// </summary>
public sealed record PublicChatQueueAlertSettings
{
    public required int StuckAfterSeconds { get; init; }
}

/// <summary>
/// Immutable, normalized Twitch bot configuration consumed outside the host boundary.
/// </summary>
public sealed record TwitchBotSettings
{
    public required TwitchBotRuntime Runtime { get; init; }

    public required int ChatMessageSendIntervalSeconds { get; init; }

    public required int DuplicateChatMessageCooldownSeconds { get; init; }

    public required int MaxChatMessageLength { get; init; }

    public required string StartupMessage { get; init; }

    public required PublicChatQueueAlertSettings PublicChatQueueAlerts { get; init; }

    public required TwitchBotConnectionSettings Connection { get; init; }

    public required TwitchBotIdentity Identity { get; init; }

    /// <summary>
    /// Maps a mutable configuration transport into an immutable snapshot for configuration-only use.
    /// </summary>
    public static TwitchBotSettings FromOptions(TwitchBotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Create(options, TwitchBotIdentity.FromOptions(options.Identity));
    }

    /// <summary>
    /// Validates and maps a mutable configuration transport into an immutable snapshot.
    /// </summary>
    public static TwitchBotSettings FromValidatedOptions(
        TwitchBotOptions options,
        string boundary,
        bool requireConfiguredIdentity
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);

        if (!TwitchBotOptionsValidation.IsValid(options))
        {
            throw new Microsoft.Extensions.Options.OptionsValidationException(
                boundary,
                typeof(TwitchBotOptions),
                ["Twitch bot options contain an invalid numeric value."]
            );
        }

        var identity = requireConfiguredIdentity
            ? TwitchBotIdentity.FromValidatedOptions(options.Identity, boundary)
            : TwitchBotIdentity.FromOptions(options.Identity);

        return Create(options, identity);
    }

    private static TwitchBotSettings Create(
        TwitchBotOptions options,
        TwitchBotIdentity identity
    ) =>
        new()
        {
            Runtime = options.Runtime,
            ChatMessageSendIntervalSeconds = options.ChatMessageSendIntervalSeconds,
            DuplicateChatMessageCooldownSeconds = options.DuplicateChatMessageCooldownSeconds,
            MaxChatMessageLength = options.MaxChatMessageLength,
            StartupMessage = options.StartupMessage,
            PublicChatQueueAlerts = new PublicChatQueueAlertSettings
            {
                StuckAfterSeconds = options.PublicChatQueueAlerts.StuckAfterSeconds,
            },
            Connection = new TwitchBotConnectionSettings
            {
                Host = options.Connection.Host.Trim(),
                Port = options.Connection.Port,
                UseTls = options.Connection.UseTls,
            },
            Identity = identity,
        };

    public override string ToString() =>
        $"{nameof(TwitchBotSettings)} {{ Runtime = {Runtime}, StartupMessage = [redacted], Identity = {Identity} }}";
}
