using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Immutable, normalized Twitch bot identity consumed by runtime services.
/// </summary>
public sealed record BotIdentity
{
    /// <summary>Gets the normalized bot account login.</summary>
    public required string BotUsername { get; init; }

    /// <summary>Gets the Twitch application client identifier.</summary>
    public required string ClientId { get; init; }

    /// <summary>Gets the Twitch application client secret.</summary>
    public required string ClientSecret { get; init; }

    /// <summary>Gets the OAuth redirect URI.</summary>
    public required string RedirectUri { get; init; }

    /// <summary>Gets the normalized, immutable OAuth scope set.</summary>
    public required OAuthScopeSet Scopes { get; init; }

    /// <summary>Gets the token storage path.</summary>
    public required string TokenCachePath { get; init; }

    /// <summary>
    /// Maps a binding DTO to a defensively copied immutable value.
    /// </summary>
    public static BotIdentity FromOptions(BotIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new BotIdentity
        {
            BotUsername = Login.Normalize(options.BotUsername),
            ClientId = (options.ClientId ?? string.Empty).Trim(),
            ClientSecret = options.ClientSecret ?? string.Empty,
            RedirectUri = (options.RedirectUri ?? string.Empty).Trim(),
            Scopes = OAuthScopeSet.Create(options.Scopes ?? []),
            TokenCachePath = (options.TokenCachePath ?? string.Empty).Trim(),
        };
    }

    /// <summary>
    /// Validates and maps a configured runtime identity.
    /// </summary>
    public static BotIdentity FromConfiguredOptions(BotIdentityOptions options, string boundary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        var validation = new BotIdentityOptionsValidator().Validate(boundary, options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                boundary,
                typeof(BotIdentityOptions),
                validation.Failures
            );
        }

        if (
            options.Scopes is null
            || options.Scopes.Length == 0
            || options.Scopes.Any(scope =>
                string.IsNullOrWhiteSpace(scope)
                || !OAuthScopeSet.IsValid(scope.Trim().ToLowerInvariant())
            )
        )
        {
            throw new OptionsValidationException(
                boundary,
                typeof(BotIdentityOptions),
                [$"{nameof(BotIdentityOptions.Scopes)} must contain only valid scopes."]
            );
        }

        return FromOptions(options);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"TwitchBotIdentity {{ BotUsername = {BotUsername}, ClientId = [redacted], ClientSecret = [redacted], Scopes = {Scopes.Count} }}";
    }
}
