using System.ComponentModel.DataAnnotations;

public sealed class TwitchBotOptions
{
    [Required]
    public ConnectionOptions Connection { get; init; } = new();

    [Required]
    public IdentityOptions Identity { get; init; } = new();

    [Required]
    public string Channel { get; init; } = string.Empty;

    [Required]
    public FiltersOptions Filters { get; init; } = new();

    [Required]
    public CountersOptions Counters { get; init; } = new();

    public sealed class ConnectionOptions
    {
        [Required]
        public string Host { get; init; } = "irc.chat.twitch.tv";

        [Range(1, 65535)]
        public int Port { get; init; } = 6667;
        public bool UseTls { get; init; } = false;
    }

    public sealed class IdentityOptions
    {
        [Required]
        public string BotUsername { get; init; } = string.Empty;

        [Required]
        public string ClientId { get; init; } = string.Empty;

        [Required]
        public string ClientSecret { get; init; } = string.Empty;

        [Required]
        public string RedirectUri { get; init; } = string.Empty;

        [MinLength(1)]
        public string[] Scopes { get; init; } = ["chat:read", "chat:edit"];

        [Required]
        public string TokenCachePath { get; init; } = "twitch.tokens.json";
    }

    public sealed class FiltersOptions
    {
        [MinLength(1)]
        public string[] AllowedLogins { get; init; } = [];
    }

    public sealed class CountersOptions
    {
        [Required]
        public string DatabasePath { get; init; } = "commandbot.db";
    }
}
