using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Alsi.TwitchBot;
using BlokeBot.Twitch;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Status;

public sealed class HostBotStatusService(
    IHttpClientFactory httpClientFactory,
    IServiceProvider services,
    TwitchTokenValidationClient tokenValidation,
    IOptions<TwitchBotOptions> options
)
{
    private readonly HttpClient http = httpClientFactory.CreateClient("twitch-helix");
    private readonly TwitchBotOptions options = options.Value;

    public async Task<HostBotChannelStatus> GetStatusAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        var flags = ConfiguredFlags();
        if ((flags & HostBotChannelStatusFlags.ModeratorCheckConfigured) == 0)
            return HostBotChannelStatus.NotConfigured();

        string token;
        try
        {
            token = await GetUserAccessTokenAsync(ct);
        }
        catch (InvalidOperationException)
        {
            return HostBotChannelStatus.NeedsAuthorization(flags);
        }

        try
        {
            var validation = await tokenValidation.ValidateAsync(token, ct);
            if (validation is null)
                return HostBotChannelStatus.NeedsAuthorization(flags);

            flags |= HostBotChannelStatusFlags.BotAccountAuthorized;
            if (validation.Scopes.Contains(TwitchScopes.UserReadModeratedChannels))
                flags |= HostBotChannelStatusFlags.ModeratorCheckGranted;
            if (validation.Scopes.Contains(TwitchScopes.ModeratorReadFollowers))
                flags |= HostBotChannelStatusFlags.FollowerReadGranted;

            if ((flags & HostBotChannelStatusFlags.ModeratorCheckGranted) == 0)
                return HostBotChannelStatus.MissingModeratorCheckPermission(flags);

            var identities = await LookupUsersAsync(
                token,
                [NormalizeLogin(channelLogin), NormalizeLogin(options.Identity.BotUsername)],
                ct
            );
            if (
                !identities.TryGetValue(NormalizeLogin(channelLogin), out var channelId)
                || !identities.TryGetValue(
                    NormalizeLogin(options.Identity.BotUsername),
                    out var botId
                )
            )
            {
                return HostBotChannelStatus.Unknown(flags);
            }

            if (!string.Equals(validation.UserId, botId, StringComparison.Ordinal))
                return HostBotChannelStatus.NeedsAuthorization(flags);

            var moderatorCheck = await BotModeratesChannelAsync(token, botId, channelId, ct);
            return moderatorCheck switch
            {
                ModeratorCheckResult.IsModerator
                    when (
                        flags
                        & (
                            HostBotChannelStatusFlags.FollowerReadConfigured
                            | HostBotChannelStatusFlags.FollowerReadGranted
                        )
                    )
                        == (
                            HostBotChannelStatusFlags.FollowerReadConfigured
                            | HostBotChannelStatusFlags.FollowerReadGranted
                        ) => HostBotChannelStatus.Ready(),
                ModeratorCheckResult.IsModerator =>
                    HostBotChannelStatus.MissingFollowerReadPermission(flags),
                ModeratorCheckResult.NotModerator => HostBotChannelStatus.NotModerator(flags),
                ModeratorCheckResult.NeedsAuthorization => HostBotChannelStatus.NeedsAuthorization(
                    flags
                ),
                ModeratorCheckResult.MissingPermission =>
                    HostBotChannelStatus.MissingModeratorCheckPermission(flags),
                _ => HostBotChannelStatus.Unknown(flags),
            };
        }
        catch
        {
            return HostBotChannelStatus.Unknown(flags);
        }
    }

    public async Task<bool> IsStreamLiveAsync(string channelLogin, CancellationToken ct)
    {
        var token = await GetAppTokenAsync(ct);
        var uri =
            "https://api.twitch.tv/helix/streams?user_login="
            + Uri.EscapeDataString(NormalizeLogin(channelLogin));
        using var request = CreateRequest(HttpMethod.Get, uri, token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TwitchStreamResponse>(ct);
        return payload?.Data.Count > 0;
    }

    public async Task<FollowerCheckResult> IsFollowerAsync(
        string channelLogin,
        string viewerLogin,
        CancellationToken ct
    )
    {
        var status = await GetStatusAsync(channelLogin, ct);
        if (status.ModeratorState != HostBotModeratorState.IsModerator)
            return FollowerCheckResult.Unavailable;

        var token = await GetUserAccessTokenAsync(ct);
        var identities = await LookupUsersAsync(
            token,
            [
                NormalizeLogin(channelLogin),
                NormalizeLogin(viewerLogin),
                NormalizeLogin(options.Identity.BotUsername),
            ],
            ct
        );
        if (
            !identities.TryGetValue(NormalizeLogin(channelLogin), out var channelId)
            || !identities.TryGetValue(NormalizeLogin(viewerLogin), out var viewerId)
            || !identities.TryGetValue(NormalizeLogin(options.Identity.BotUsername), out var botId)
        )
        {
            return FollowerCheckResult.NotEligible;
        }

        var uri =
            "https://api.twitch.tv/helix/channels/followers"
            + $"?broadcaster_id={Uri.EscapeDataString(channelId)}"
            + $"&user_id={Uri.EscapeDataString(viewerId)}"
            + $"&moderator_id={Uri.EscapeDataString(botId)}";
        using var request = CreateRequest(HttpMethod.Get, uri, token);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            return FollowerCheckResult.Unavailable;

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TwitchFollowerResponse>(ct);
        return payload?.Data.Count > 0
            ? FollowerCheckResult.Eligible
            : FollowerCheckResult.NotEligible;
    }

    private HostBotChannelStatusFlags ConfiguredFlags()
    {
        var flags = HostBotChannelStatusFlags.None;
        foreach (
            var scope in options.Identity.Scopes.Select(TwitchTokenValidationClient.NormalizeScope)
        )
        {
            flags |= scope switch
            {
                TwitchScopes.UserReadModeratedChannels =>
                    HostBotChannelStatusFlags.ModeratorCheckConfigured,
                TwitchScopes.ModeratorReadFollowers =>
                    HostBotChannelStatusFlags.FollowerReadConfigured,
                _ => HostBotChannelStatusFlags.None,
            };
        }

        return flags;
    }

    private async Task<ModeratorCheckResult> BotModeratesChannelAsync(
        string token,
        string botId,
        string channelId,
        CancellationToken ct
    )
    {
        string? cursor = null;
        do
        {
            var uri =
                "https://api.twitch.tv/helix/moderation/channels"
                + $"?user_id={Uri.EscapeDataString(botId)}"
                + "&first=100"
                + (
                    string.IsNullOrWhiteSpace(cursor)
                        ? string.Empty
                        : $"&after={Uri.EscapeDataString(cursor)}"
                );
            using var request = CreateRequest(HttpMethod.Get, uri, token);
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return ModeratorCheckResult.NeedsAuthorization;

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return ModeratorCheckResult.MissingPermission;

            if (!response.IsSuccessStatusCode)
                return ModeratorCheckResult.Unknown;

            var payload = await response.Content.ReadFromJsonAsync<TwitchModeratedChannelsResponse>(
                ct
            );
            if (
                payload?.Data.Any(x =>
                    string.Equals(x.BroadcasterId, channelId, StringComparison.Ordinal)
                ) == true
            )
                return ModeratorCheckResult.IsModerator;

            cursor = payload?.Pagination.Cursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return ModeratorCheckResult.NotModerator;
    }

    private async Task<string> GetAppTokenAsync(CancellationToken ct)
    {
        var appTokens = services.GetService<TwitchAppAccessTokenProvider>();
        if (appTokens is null)
            throw new InvalidOperationException("Twitch bot runtime is not configured.");

        return await appTokens.GetAccessTokenAsync(ct);
    }

    private async Task<string> GetUserAccessTokenAsync(CancellationToken ct)
    {
        var userToken = services.GetService<ITwitchAccessTokenProvider>();
        if (userToken is null)
            throw new InvalidOperationException("Twitch bot runtime is not configured.");

        return await userToken.GetAccessTokenAsync(ct);
    }

    private async Task<Dictionary<string, string>> LookupUsersAsync(
        string token,
        IReadOnlyList<string> logins,
        CancellationToken ct
    )
    {
        var uri =
            "https://api.twitch.tv/helix/users?"
            + string.Join('&', logins.Distinct().Select(x => $"login={Uri.EscapeDataString(x)}"));
        using var request = CreateRequest(HttpMethod.Get, uri, token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TwitchUsersResponse>(ct);
        return payload?.Data.ToDictionary(x => x.Login, x => x.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", options.Identity.ClientId);
        return request;
    }

    private static string NormalizeLogin(string value) =>
        value.Trim().TrimStart('#').ToLowerInvariant();

    private sealed record TwitchStreamResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<object> Data
    );

    private sealed record TwitchFollowerResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<object> Data
    );

    private sealed record TwitchUsersResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<TwitchUser> Data
    );

    private sealed record TwitchUser(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("login")] string Login
    );

    private sealed record TwitchModeratedChannelsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<TwitchModeratedChannel> Data,
        [property: JsonPropertyName("pagination")] TwitchPagination Pagination
    );

    private sealed record TwitchModeratedChannel(
        [property: JsonPropertyName("broadcaster_id")] string BroadcasterId
    );

    private sealed record TwitchPagination([property: JsonPropertyName("cursor")] string? Cursor);

    private enum ModeratorCheckResult
    {
        Unknown,
        NeedsAuthorization,
        MissingPermission,
        IsModerator,
        NotModerator,
    }
}
