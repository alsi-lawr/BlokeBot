using System.ComponentModel.DataAnnotations;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Configures the Twitch bot identity and OAuth client.
/// </summary>
public sealed record TwitchBotIdentityOptions
{
    /// <summary>
    /// Creates Twitch identity options.
    /// </summary>
    public TwitchBotIdentityOptions() { }

    /// <summary>
    /// Gets the bot account login.
    /// </summary>
    [Required]
    public string BotUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets the Twitch application client identifier.
    /// </summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the Twitch application client secret.
    /// </summary>
    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets the OAuth redirect URI.
    /// </summary>
    [Required]
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets the OAuth scopes requested for the bot account.
    /// </summary>
    [MinLength(1)]
    public string[] Scopes { get; set; } =
    ["chat:read", "chat:edit", TwitchScopes.UserReadModeratedChannels];

    /// <summary>
    /// Gets the token storage path used by the default token store.
    /// </summary>
    [Required]
    public string TokenCachePath { get; set; } = "twitch.tokens.json";
}
