namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Immutable IRC connection settings consumed by the runtime.
/// </summary>
public sealed record IrcConnectionSettings
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
public sealed record BotSettings
{
    public required ChatRuntime Runtime { get; init; }

    public required int ChatMessageSendIntervalSeconds { get; init; }

    public required int DuplicateChatMessageCooldownSeconds { get; init; }

    public required int MaxChatMessageLength { get; init; }

    public required string StartupMessage { get; init; }

    public required PublicChatQueueAlertSettings PublicChatQueueAlerts { get; init; }

    public required IrcConnectionSettings Connection { get; init; }

    public required BotIdentity Identity { get; init; }

    public EventSubWebhookOptions? EventSubWebhook { get; init; }

    /// <summary>
    /// Maps a mutable configuration transport into an immutable snapshot for configuration-only use.
    /// </summary>
    public static BotSettings FromOptions(BotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Create(options, BotIdentity.FromOptions(options.Identity));
    }

    /// <summary>
    /// Validates and maps a mutable configuration transport into an immutable snapshot.
    /// </summary>
    public static BotSettings FromConfiguredOptions(
        BotOptions options,
        string boundary,
        bool online = true
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);

        if (!BotOptionsValidation.IsValid(options))
        {
            throw new Microsoft.Extensions.Options.OptionsValidationException(
                boundary,
                typeof(BotOptions),
                ["Twitch bot options contain an invalid value."]
            );
        }

        var webhook =
            options.EventSubWebhook
            ?? throw new Microsoft.Extensions.Options.OptionsValidationException(
                boundary,
                typeof(BotOptions),
                ["TwitchBot.EventSubWebhook configuration is required."]
            );
        try
        {
            webhook.Validate(online);
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            throw new Microsoft.Extensions.Options.OptionsValidationException(
                boundary,
                typeof(BotOptions),
                ["TwitchBot.EventSubWebhook configuration is invalid."]
            );
        }

        return Create(
            options,
            BotIdentity.FromConfiguredOptions(options.Identity, boundary),
            webhook
        );
    }

    private static BotSettings Create(
        BotOptions options,
        BotIdentity identity,
        EventSubWebhookOptions? webhook = null
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
            Connection = new IrcConnectionSettings
            {
                Host = options.Connection.Host.Trim(),
                Port = options.Connection.Port,
                UseTls = options.Connection.UseTls,
            },
            Identity = identity,
            EventSubWebhook = webhook,
        };

    public override string ToString() =>
        $"TwitchBotSettings {{ Runtime = {Runtime}, StartupMessage = [redacted], Identity = {Identity} }}";
}
