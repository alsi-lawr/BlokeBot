using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class TwitchHelixApiClient(IHttpClientFactory httpClientFactory)
{
    private const string _usersEndpoint = "https://api.twitch.tv/helix/users";
    private const string _streamsEndpoint = "https://api.twitch.tv/helix/streams";
    private const string _followersEndpoint = "https://api.twitch.tv/helix/channels/followers";
    private const string _moderatedChannelsEndpoint =
        "https://api.twitch.tv/helix/moderation/channels";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public async Task<TwitchHelixUser?> GetCurrentUserAsync(
        TwitchHelixRequestContext context,
        CancellationToken cancellationToken
    )
    {
        using var request = CreateRequest(HttpMethod.Get, _usersEndpoint, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TwitchUsersResponse>(
            _jsonOptions,
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
        {
            return [];
        }

        var uri =
            $"{_usersEndpoint}?"
            + TwitchQueryString.Create(
                normalized.Select(login => new KeyValuePair<string, string?>("login", login))
            );
        using var request = CreateRequest(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TwitchUsersResponse>(
            _jsonOptions,
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
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<TwitchModeratedChannelsResponse>(
                _jsonOptions,
                cancellationToken
            );
            if (payload?.Data is { Count: > 0 } data)
            {
                channels.AddRange(data);
            }

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
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return TwitchModeratedChannelStatus.NeedsAuthorization;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return TwitchModeratedChannelStatus.MissingPermission;
            }

            if (!response.IsSuccessStatusCode)
            {
                return TwitchModeratedChannelStatus.Unknown;
            }

            var payload = await response.Content.ReadFromJsonAsync<TwitchModeratedChannelsResponse>(
                _jsonOptions,
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
            $"{_streamsEndpoint}?"
            + TwitchQueryString.Create([
                new KeyValuePair<string, string?>(
                    "user_login",
                    TwitchLogin.Normalize(channelLogin)
                ),
            ]);
        using var request = CreateRequest(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TwitchStreamResponse>(
            _jsonOptions,
            cancellationToken
        );
        return payload?.Data.Length > 0;
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
            $"{_followersEndpoint}?"
            + TwitchQueryString.Create(
                new Dictionary<string, string?>
                {
                    ["broadcaster_id"] = broadcasterId,
                    ["moderator_id"] = moderatorId,
                    ["user_id"] = userId,
                }
            );
        using var request = CreateRequest(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return TwitchFollowerStatus.Unavailable;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TwitchFollowerResponse>(
            _jsonOptions,
            cancellationToken
        );
        return payload?.Data.Length > 0
            ? TwitchFollowerStatus.Follows
            : TwitchFollowerStatus.DoesNotFollow;
    }

    private static string ModeratedChannelsUri(string userId, string? cursor)
    {
        var query = new Dictionary<string, string?> { ["first"] = "100", ["user_id"] = userId };

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query["after"] = cursor;
        }

        return $"{_moderatedChannelsEndpoint}?{TwitchQueryString.Create(query)}";
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string uri,
        TwitchHelixRequestContext context
    )
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            context.AccessToken
        );
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
        public required ImmutableArray<TwitchStreamItem> Data { get; init; }
    }

    private sealed record TwitchFollowerResponse
    {
        [JsonPropertyName("data")]
        public required ImmutableArray<TwitchFollowerItem> Data { get; init; }
    }

    private sealed record TwitchStreamItem
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("user_id")]
        public required string UserId { get; init; }

        [JsonPropertyName("user_login")]
        public required string UserLogin { get; init; }

        [JsonPropertyName("user_name")]
        public required string UserName { get; init; }

        [JsonPropertyName("game_id")]
        public required string GameId { get; init; }

        [JsonPropertyName("game_name")]
        public required string GameName { get; init; }

        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("tags")]
        public required ImmutableArray<string> Tags { get; init; }

        [JsonPropertyName("viewer_count")]
        public required int ViewerCount { get; init; }

        [JsonPropertyName("started_at")]
        public required DateTimeOffset StartedAt { get; init; }

        [JsonPropertyName("language")]
        public required string Language { get; init; }

        [JsonPropertyName("thumbnail_url")]
        public required string ThumbnailUrl { get; init; }

        [JsonPropertyName("is_mature")]
        public required bool IsMature { get; init; }
    }

    private sealed record TwitchFollowerItem
    {
        [JsonPropertyName("user_id")]
        public required string UserId { get; init; }

        [JsonPropertyName("user_login")]
        public required string UserLogin { get; init; }

        [JsonPropertyName("user_name")]
        public required string UserName { get; init; }

        [JsonPropertyName("followed_at")]
        public required DateTimeOffset FollowedAt { get; init; }
    }

    private sealed record TwitchPagination
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; init; }
    }
}
