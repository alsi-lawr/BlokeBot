using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Immutable, normalized Twitch bot identity consumed by runtime services.
/// </summary>
public sealed record TwitchBotIdentity
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
    public required ImmutableArray<string> Scopes { get; init; }

    /// <summary>Gets the token storage path.</summary>
    public required string TokenCachePath { get; init; }

    /// <summary>
    /// Maps a binding DTO to a defensively copied immutable value.
    /// </summary>
    public static TwitchBotIdentity FromOptions(TwitchBotIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TwitchBotIdentity
        {
            BotUsername = Login.Normalize(options.BotUsername),
            ClientId = (options.ClientId ?? string.Empty).Trim(),
            ClientSecret = options.ClientSecret ?? string.Empty,
            RedirectUri = (options.RedirectUri ?? string.Empty).Trim(),
            Scopes = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(options.Scopes ?? [])),
            TokenCachePath = (options.TokenCachePath ?? string.Empty).Trim(),
        };
    }

    /// <summary>
    /// Validates and maps a configured runtime identity.
    /// </summary>
    public static TwitchBotIdentity FromValidatedOptions(
        TwitchBotIdentityOptions options,
        string boundary
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        var validation = new TwitchBotIdentityOptionsValidator().Validate(boundary, options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                boundary,
                typeof(TwitchBotIdentityOptions),
                validation.Failures
            );
        }

        var identity = FromOptions(options);
        if (identity.Scopes.IsEmpty)
        {
            throw new OptionsValidationException(
                boundary,
                typeof(TwitchBotIdentityOptions),
                [$"{nameof(TwitchBotIdentityOptions.Scopes)} must contain a non-blank scope."]
            );
        }

        return identity;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{nameof(TwitchBotIdentity)} {{ BotUsername = {BotUsername}, ClientId = [redacted], ClientSecret = [redacted], Scopes = {Scopes.Length} }}";
    }
}
