namespace Alsi.TwitchBot;

/// <summary>
/// Contains Twitch OAuth tokens and their expiration time.
/// </summary>
/// <param name="AccessToken">The access token.</param>
/// <param name="RefreshToken">The refresh token.</param>
/// <param name="ExpiresAtUtc">The UTC expiration time.</param>
public sealed record TwitchTokenSet(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc
);
