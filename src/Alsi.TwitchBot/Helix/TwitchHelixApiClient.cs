using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

public sealed class TwitchHelixApiClient(IHttpClientFactory httpClientFactory)
{
    private const string UsersEndpoint = "https://api.twitch.tv/helix/users";
    private const string StreamsEndpoint = "https://api.twitch.tv/helix/streams";
    private const string FollowersEndpoint = "https://api.twitch.tv/helix/channels/followers";
    private const string ModeratedChannelsEndpoint =
        "https://api.twitch.tv/helix/moderation/channels";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = httpClientFactory.CreateClient("twitch-helix");

    public async Task<TwitchHelixUser?> GetCurrentUserAsync(
        TwitchHelixRequestContext context,
        CancellationToken cancellationToken
    )
    {
        using var request = CreateRequest(HttpMethod.Get, UsersEndpoint, context);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TwitchUsersResponse>(
            JsonOptions,
            cancellationToken
        );
        return payload?.Data.FirstOrDefault();
    }

    public async Task<IReadOnlyList<TwitchHelixUser>> GetUsersByLoginAsync(
        TwitchHelixRequestContext context,
        IEnumerable<string?> logins,
        CancellationToken cancellationToken
    )
    {
        var normalized = TwitchLogin.NormalizeMany(logins);
        if (normalized.Length == 0)
            return [];

        var uri =
            $"{UsersEndpoint}?"
            + TwitchQueryString.Create(normalized.Select(login => new KeyValuePair<string, string?>("login", login)));
        using var request = CreateRequest(HttpMethod.Get, uri, context);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TwitchUsersResponse>(
            JsonOptions,
            cancellationToken
        );
        return payload?.Data ?? [];
    }

    public async Task<IReadOnlyList<TwitchModeratedChannel>> GetModeratedChannelsAsync(
        TwitchHelixRequestContext context,
        string userId,
        CancellationToken cancellationToken
    )
    {
        var channels = new List<TwitchModeratedChannel>();
        string? cursor = null;
        do
        {
            using var request = CreateRequest(
                HttpMethod.Get,
                ModeratedChannelsUri(userId, cursor),
                context
            );
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<TwitchModeratedChannelsResponse>(
                JsonOptions,
                cancellationToken
            );
            if (payload?.Data is { Count: > 0 } data)
                channels.AddRange(data);

            cursor = payload?.Pagination.Cursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return channels;
    }

    public async Task<TwitchModeratedChannelStatus> GetModeratedChannelStatusAsync(
        TwitchHelixRequestContext context,
        string userId,
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        string? cursor = null;
        do
        {
            using var request = CreateRequest(
                HttpMethod.Get,
                ModeratedChannelsUri(userId, cursor),
                context
            );
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return TwitchModeratedChannelStatus.NeedsAuthorization;

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return TwitchModeratedChannelStatus.MissingPermission;

            if (!response.IsSuccessStatusCode)
                return TwitchModeratedChannelStatus.Unknown;

            var payload = await response.Content.ReadFromJsonAsync<TwitchModeratedChannelsResponse>(
                JsonOptions,
                cancellationToken
            );
            if (
                payload?.Data.Any(channel =>
                    string.Equals(channel.BroadcasterId, broadcasterId, StringComparison.Ordinal)
                ) == true
            )
            {
                return TwitchModeratedChannelStatus.IsModerator;
            }

            cursor = payload?.Pagination.Cursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return TwitchModeratedChannelStatus.NotModerator;
    }

    public async Task<bool> IsStreamLiveAsync(
        TwitchHelixRequestContext context,
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        var uri =
            $"{StreamsEndpoint}?"
            + TwitchQueryString.Create(
                [new KeyValuePair<string, string?>("user_login", TwitchLogin.Normalize(channelLogin))]
            );
        using var request = CreateRequest(HttpMethod.Get, uri, context);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TwitchStreamResponse>(
            JsonOptions,
            cancellationToken
        );
        return payload?.Data.Count > 0;
    }

    public async Task<TwitchFollowerStatus> GetFollowerStatusAsync(
        TwitchHelixRequestContext context,
        string broadcasterId,
        string userId,
        string moderatorId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            $"{FollowersEndpoint}?"
            + TwitchQueryString.Create(
                new Dictionary<string, string?>
                {
                    ["broadcaster_id"] = broadcasterId,
                    ["moderator_id"] = moderatorId,
                    ["user_id"] = userId,
                }
            );
        using var request = CreateRequest(HttpMethod.Get, uri, context);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            return TwitchFollowerStatus.Unavailable;

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TwitchFollowerResponse>(
            JsonOptions,
            cancellationToken
        );
        return payload?.Data.Count > 0
            ? TwitchFollowerStatus.Follows
            : TwitchFollowerStatus.DoesNotFollow;
    }

    private static string ModeratedChannelsUri(string userId, string? cursor)
    {
        var query = new Dictionary<string, string?> { ["first"] = "100", ["user_id"] = userId };

        if (!string.IsNullOrWhiteSpace(cursor))
            query["after"] = cursor;

        return $"{ModeratedChannelsEndpoint}?{TwitchQueryString.Create(query)}";
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string uri,
        TwitchHelixRequestContext context
    )
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Add("Client-Id", context.ClientId);
        return request;
    }

    private sealed record TwitchUsersResponse
    {
        [JsonPropertyName("data")]
        public IReadOnlyList<TwitchHelixUser> Data { get; init; } = [];
    }

    private sealed record TwitchModeratedChannelsResponse
    {
        [JsonPropertyName("data")]
        public IReadOnlyList<TwitchModeratedChannel> Data { get; init; } = [];

        [JsonPropertyName("pagination")]
        public TwitchPagination Pagination { get; init; } = new();
    }

    private sealed record TwitchStreamResponse
    {
        [JsonPropertyName("data")]
        public IReadOnlyList<object> Data { get; init; } = [];
    }

    private sealed record TwitchFollowerResponse
    {
        [JsonPropertyName("data")]
        public IReadOnlyList<object> Data { get; init; } = [];
    }

    private sealed record TwitchPagination
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; init; }
    }
}
