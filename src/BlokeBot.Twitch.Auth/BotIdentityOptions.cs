using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Configures the Twitch bot identity and OAuth client.
/// </summary>
public sealed record BotIdentityOptions
{
    /// <summary>
    /// Creates Twitch identity options.
    /// </summary>
    [SetsRequiredMembers]
    public BotIdentityOptions() { }

    /// <summary>
    /// Gets the bot account login.
    /// </summary>
    [Required]
    public required string BotUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets the Twitch application client identifier.
    /// </summary>
    [Required]
    public required string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the Twitch application client secret.
    /// </summary>
    [Required]
    public required string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets the OAuth redirect URI.
    /// </summary>
    [Required]
    public required string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets the OAuth scopes requested for the bot account.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string[] Scopes { get; set; } =
    [
        "chat:read",
        "chat:edit",
        BlokeBot.Twitch.Scopes.ModeratorManageAnnouncements,
        BlokeBot.Twitch.Scopes.UserReadFollows,
        BlokeBot.Twitch.Scopes.UserReadModeratedChannels,
    ];

    /// <summary>
    /// Gets the token storage path used by the default token store.
    /// </summary>
    [Required]
    public required string TokenCachePath { get; set; } = "twitch.tokens.json";
}

/// <summary>
/// Performs source-generated validation for Twitch identity configuration.
/// </summary>
[OptionsValidator]
public sealed partial class BotIdentityOptionsValidator : IValidateOptions<BotIdentityOptions> { }
